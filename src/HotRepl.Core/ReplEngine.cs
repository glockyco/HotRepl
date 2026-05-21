using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Fleck;
using HotRepl.Control;
using HotRepl.Control.Jobs;
using HotRepl.Engine.Commands;
using HotRepl.Evaluator;
using HotRepl.Helpers;
using HotRepl.Protocol;
using HotRepl.Serialization;
using HotRepl.Server;
using HotRepl.Subscriptions;

namespace HotRepl;

/// <summary>
/// Composition root. Owns all subsystems; wired up in Start(), driven by Tick().
///
/// Threading model:
///   Fleck threads  → EnqueueEval / CancelEval / EnqueueCommand (non-blocking)
///   Main thread    → Start / Tick / Dispose; executes evals, subscriptions,
///                    control commands, and control jobs
///   Watchdog timer → may call Thread.Abort on the main thread
///
/// Tick() drain order (invariant):
///   1. Process cancel requests — populate _cancelledIds, abort if matching eval running
///   2. Drain command queue — reset, ping, complete, subscribe, control
///   3. Start at most one accepted control job
///   4. Execute at most one eval
///   5. Tick subscriptions
/// </summary>
public sealed class ReplEngine : IDisposable
{
    private readonly IReplHost _host;
    public ReplConfig Config => _host.Config;

    // ── Subsystems (created in Start) ─────────────────────────────────────────
    private ReplWebSocketServer? _wsServer;
    private ClientRegistry? _clients;
    private MessageRouter? _router;
    private ICodeEvaluator? _evaluator;
    private SubscriptionManager? _subscriptions;
    private HistoryTracker? _history;
    private ControlCommandRouter? _controlRouter;
    private ControlSessionManager? _controlSessions;
    private ControlJobManager? _controlJobs;

    // ── Queues — written by Fleck threads, drained by Tick() ──────────────────
    private readonly ConcurrentQueue<EvalJob> _evalQueue = new();
    private readonly ConcurrentQueue<IEngineCommand> _commandQueue = new();
    private readonly ConcurrentQueue<string> _jobRunQueue = new();

    // Cancel: populated by Fleck threads via CancelEval(); checked by Tick().
    // ConcurrentDictionary used as a concurrent set (value is ignored).
    private readonly ConcurrentDictionary<string, bool> _cancelledIds = new(StringComparer.Ordinal);

    // ── Watchdog state — protected by _abortLock ─────────────────────────────
    private readonly object _abortLock = new();
    private Thread? _mainThread;
    private bool _evalInProgress;
    private string? _currentEvalId;
    private CancellationTokenSource? _currentCancellation;
    private long _currentGeneration;
    private volatile bool _timedOut;

    private bool _evaluatorReady;
    private bool _disposed;

    public ReplEngine(IReplHost host) => _host = host;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Wires subsystems and starts the WebSocket server.
    /// Must be called from the main thread (captures Thread.CurrentThread for abort).
    /// Awake() calls this directly.
    /// </summary>
    public void Start()
    {
        _mainThread = Thread.CurrentThread;

        _history = new HistoryTracker();
        _subscriptions = new SubscriptionManager(_host.Config);
        _evaluator = CreateEvaluator(_host.DefaultEvaluatorName);

        _wsServer = new ReplWebSocketServer(msg => _host.LogInfo(msg));
        _clients = new ClientRegistry(_wsServer, msg => _host.LogInfo(msg));
        _router = new MessageRouter(this, msg => _host.LogInfo(msg));
        _controlSessions = new ControlSessionManager(_host.Config);
        _controlJobs = new ControlJobManager(_host.Config.MaxJobEventBuffer);
        _controlRouter = new ControlCommandRouter(
            _host.ControlCommands,
            _controlSessions,
            _controlJobs
        );

        _wsServer.ClientConnected += (_, e) => OnClientConnected(e.ConnectionId, e.Connection);
        _wsServer.ClientDisconnected += (_, e) => _clients.OnDisconnected(e.ConnectionId);
        _wsServer.ClientDisconnected += (_, e) => _controlSessions.OnDisconnected(e.ConnectionId);
        _wsServer.MessageReceived += (_, e) => _router.HandleMessage(e.ConnectionId, e.RawJson);

        _wsServer.Start(_host.Config.Port, _host.Config.BindHost);
        _host.LogInfo($"[HotRepl] Engine started on port {_host.Config.Port}.");
    }

    /// <summary>
    /// Called once per frame from Unity Update().
    /// Initializes the evaluator on the first call, then processes queued work.
    /// </summary>
    public void Tick()
    {
        if (_disposed)
            return;
        if (_wsServer == null)
            return; // Start() not yet called

        // First Tick: initialize the evaluator (deferred from Awake for speed).
        if (!_evaluatorReady)
        {
            InitializeEvaluator();
            _evaluatorReady = true;
        }

        // 1. Cancel drain — must run before eval dequeue so cancels applied in
        //    this Tick are visible when we check the eval queue in step 3.
        //    (CancelEval() already writes to _cancelledIds immediately from the
        //    Fleck thread, so this step is mainly for abort-running-eval handling
        //    that couldn't fire on the Fleck thread for ordering reasons.)

        // 2. Command drain.
        while (_commandQueue.TryDequeue(out var cmd))
            HandleCommand(cmd);

        if (_jobRunQueue.TryDequeue(out var jobId))
            _ = _controlRouter!.RunJobAsync(jobId).AsTask();

        // 2b. Auto-reset on hot reload. ScriptEngine assembly loads set the
        //     flag during OnAssemblyLoad. Reset here so subsequent evals
        //     resolve types from the newest assembly.
        if (_evaluator!.PendingHotReload)
            HandleHotReload();

        // 3. At most one eval per Tick.
        DrainOneEval();

        // 4. Subscriptions.
        _subscriptions!.Tick(
            (id, code, timeoutMs) => GuardedEvaluate(id, code, timeoutMs),
            (connId, json) => _clients!.SendTo(connId, json)
        );

        // 5. Drain stale cancel IDs. Any cancel received during this Tick has
        //    already preempted a queued eval or aborted the running one.
        //    Leftover entries are for evals that finished or never existed.
        if (!_cancelledIds.IsEmpty)
            _cancelledIds.Clear();
    }

    private void DrainOneEval()
    {
        while (_evalQueue.TryDequeue(out var job))
        {
            if (_cancelledIds.TryRemove(job.Id, out _))
            {
                using (job)
                {
                    // Cancelled before it ever ran.
                    _clients!.SendTo(
                        job.ConnectionId,
                        MessageSerializer.Serialize(
                            new EvalErrorMessage
                            {
                                Id = job.Id,
                                ErrorKind = ErrorKind.Cancelled,
                                Message = "Evaluation cancelled.",
                            }
                        )
                    );
                }
                continue; // try next in queue — but only process one non-cancelled eval
            }
            ExecuteEval(job);
            return;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _wsServer?.Dispose();
        _evaluator?.Dispose();
    }

    // ── Public API for MessageRouter (called from Fleck threads) ─────────────

    internal void EnqueueEval(EvalJob job) => _evalQueue.Enqueue(job);

    /// <summary>
    /// Records the id as cancelled and immediately aborts the main thread if
    /// that eval is currently running. Thread-safe; called on Fleck threads.
    /// </summary>
    internal void CancelEval(string id)
    {
        _cancelledIds.TryAdd(id, true);

        lock (_abortLock)
        {
            if (_evalInProgress && string.Equals(_currentEvalId, id, StringComparison.Ordinal))
            {
                if (_evaluator?.Capabilities.TimeoutMode == TimeoutMode.HardAbort)
#pragma warning disable MA0035 // HardAbort timeout mode is the documented design — see AGENTS.md "Evaluator timeout is capability-driven".
                    _mainThread?.Abort();
#pragma warning restore MA0035
                else if (_evaluator?.Capabilities.TimeoutMode == TimeoutMode.Cooperative)
                    _currentCancellation?.Cancel();
            }
        }
    }

    internal void EnqueueCommand(IEngineCommand cmd) => _commandQueue.Enqueue(cmd);

    private ICodeEvaluator CreateEvaluator(string evaluatorName)
    {
        var available = _host.AvailableEvaluators.Select(c => c.Name).ToArray();
        if (!available.Contains(evaluatorName, StringComparer.Ordinal))
            throw new NotSupportedException(
                $"Evaluator '{evaluatorName}' is not available. Available: {string.Join(", ", available)}"
            );

        return _host.CreateEvaluator(evaluatorName);
    }

    // ── Private: evaluator initialization ────────────────────────────────────

    private void InitializeEvaluator()
    {
        try
        {
            _evaluator!.Initialize();
            HelperInjector.Inject(_evaluator, _host, _history!, _host.Config);
        }
        catch (Exception ex)
        {
            _host.LogError("[HotRepl] Evaluator initialization failed.", ex);
        }
    }

    // ── Private: eval execution ───────────────────────────────────────────────

    private void ExecuteEval(EvalJob job)
    {
        using (job)
        {
            EvalOutcome outcome = RunGuarded(job.Id, job.Code, job.TimeoutMs, job.Cancellation);
            RecordHistory(job.Code, outcome);
            SendEvalOutcome(job.Id, job.ConnectionId, outcome);
        }
    }

    /// <summary>
    /// Core execution primitive reused for both regular evals and subscriptions.
    /// Sets up the watchdog, calls the evaluator, and resolves the Aborted sentinel
    /// (returned by Evaluate() after it catches ThreadAbortException and calls ResetAbort)
    /// into either Timeout or Cancelled based on watchdog state.
    /// </summary>
    private EvalOutcome GuardedEvaluate(string id, string code, int timeoutMs)
    {
        using var cancellation = new CancellationTokenSource();
        return RunGuarded(id, code, timeoutMs, cancellation);
    }

    private EvalOutcome RunGuarded(
        string id,
        string code,
        int timeoutMs,
        CancellationTokenSource cancellation
    )
    {
        long gen;
        lock (_abortLock)
        {
            _evalInProgress = true;
            _currentEvalId = id;
            gen = ++_currentGeneration;
            _currentCancellation = cancellation;
        }
        _timedOut = false;

        var sw = Stopwatch.StartNew();
        using var watchdog = StartTimeoutWatchdog(timeoutMs, gen, cancellation);

        try
        {
            var outcome = _evaluator!.Evaluate(code, cancellation.Token);

            // Evaluate() catches ThreadAbortException internally, calls ResetAbort(),
            // and returns Aborted as a sentinel. Resolve it here using _timedOut.
            if (ReferenceEquals(outcome, EvalOutcome.Aborted))
            {
                sw.Stop();
                return _timedOut
                    ? EvalOutcome.Timeout(sw.ElapsedMilliseconds)
                    : EvalOutcome.Cancelled(sw.ElapsedMilliseconds);
            }

            return outcome;
        }
        finally
        {
            // The `using var watchdog` above disposes before we enter this block,
            // so a late-firing timer cannot race with a subsequent eval's setup.
            lock (_abortLock)
            {
                _evalInProgress = false;
                _currentEvalId = null;
                _currentCancellation = null;
            }
        }
    }

    private Timer StartTimeoutWatchdog(
        int timeoutMs,
        long gen,
        CancellationTokenSource cancellation
    )
    {
        return new Timer(
            _ =>
            {
                lock (_abortLock)
                {
                    if (_evalInProgress && _currentGeneration == gen)
                    {
                        _timedOut = true;
                        if (_evaluator!.Capabilities.TimeoutMode == TimeoutMode.HardAbort)
#pragma warning disable MA0035 // HardAbort timeout mode is the documented design — see AGENTS.md "Evaluator timeout is capability-driven".
                            _mainThread?.Abort();
#pragma warning restore MA0035
                        else if (_evaluator.Capabilities.TimeoutMode == TimeoutMode.Cooperative)
                            cancellation.Cancel();
                    }
                }
            },
            null,
            timeoutMs,
            Timeout.Infinite
        );
    }

    // ── Private: command handling ─────────────────────────────────────────────

    private void HandleCommand(IEngineCommand cmd)
    {
        switch (cmd)
        {
            case ResetCmd r:
                HandleReset(r);
                break;
            case PingCmd p:
                _clients!.SendTo(
                    p.ConnectionId,
                    MessageSerializer.Serialize(new PongMessage { Id = p.Id })
                );
                break;
            case CompleteCmd c:
                HandleComplete(c);
                break;
            case SubscribeCmd s:
                HandleSubscribe(s);
                break;
            case SelectEvaluatorCmd s:
                HandleSelectEvaluator(s);
                break;
            case CommandDescribeCmd c:
                _clients!.SendControlTo(
                    c.ConnectionId,
                    MessageSerializer.Serialize(_controlRouter!.Describe(c.Id))
                );
                break;
            case CommandCallCmd c:
            {
                var response = _controlRouter!.Execute(c.Message, c.ConnectionId);
                _clients!.SendControlTo(c.ConnectionId, MessageSerializer.Serialize(response));
                if (response is CommandAcceptedMessage accepted)
                    _jobRunQueue.Enqueue(accepted.JobId);
                break;
            }
            case ControlAuthCmd c:
                HandleControlAuth(c);
                break;
            case LeaseAcquireCmd c:
                HandleLeaseAcquire(c);
                break;
            case JobStatusCmd c:
                HandleJobStatus(c);
                break;
            case JobResultCmd c:
                HandleJobResult(c);
                break;
            case JobCancelCmd c:
                HandleJobCancel(c);
                break;
        }
    }

    private void HandleControlAuth(ControlAuthCmd cmd)
    {
        var result = _controlSessions!.Authenticate(cmd.ConnectionId, cmd.Token);
        _clients!.SendControlTo(
            cmd.ConnectionId,
            MessageSerializer.Serialize(
                new ControlAuthResultMessage
                {
                    Id = cmd.Id,
                    Ok = result.Ok,
                    SessionId = result.SessionId,
                    Error = result.Error == null ? null : ToControlErrorMessage(result.Error),
                }
            )
        );
    }

    private void HandleLeaseAcquire(LeaseAcquireCmd cmd)
    {
        var result = _controlSessions!.AcquireLease(
            cmd.ConnectionId,
            cmd.SessionId,
            cmd.ClientName
        );
        _clients!.SendControlTo(
            cmd.ConnectionId,
            MessageSerializer.Serialize(
                new LeaseAcquireResultMessage
                {
                    Id = cmd.Id,
                    Ok = result.Ok,
                    LeaseId = result.LeaseId,
                    Error = result.Error == null ? null : ToControlErrorMessage(result.Error),
                }
            )
        );
    }

    private void HandleJobStatus(JobStatusCmd cmd)
    {
        var response = _controlRouter!.GetJobStatus(cmd.Message, cmd.ConnectionId);
        _clients!.SendControlTo(cmd.ConnectionId, MessageSerializer.Serialize(response));
    }

    private void HandleJobResult(JobResultCmd cmd)
    {
        var response = _controlRouter!.GetJobResult(cmd.Message, cmd.ConnectionId);
        _clients!.SendControlTo(cmd.ConnectionId, MessageSerializer.Serialize(response));
    }

    private void HandleJobCancel(JobCancelCmd cmd)
    {
        var response = _controlRouter!.CancelJob(cmd.Message, cmd.ConnectionId);
        _clients!.SendControlTo(cmd.ConnectionId, MessageSerializer.Serialize(response));
    }

    private static ControlErrorMessage ToControlErrorMessage(ControlCommandError error) =>
        new()
        {
            Kind = error.Kind,
            Code = error.Code,
            Message = error.Message,
            Retryable = error.Retryable,
            Details = error.Details,
        };

    private void HandleReset(ResetCmd cmd)
    {
        // Drain and cancel all pending evals.
        while (_evalQueue.TryDequeue(out var job))
        {
            using (job)
            {
                _clients!.SendTo(
                    job.ConnectionId,
                    MessageSerializer.Serialize(
                        new EvalErrorMessage
                        {
                            Id = job.Id,
                            ErrorKind = ErrorKind.Cancelled,
                            Message = "Reset in progress.",
                        }
                    )
                );
            }
        }
        // Cancel all subscriptions with a final error.
        foreach (var sub in GetAllSubscriptions())
        {
            _clients!.SendTo(
                sub.ConnectionId,
                MessageSerializer.Serialize(
                    new SubscribeErrorMessage
                    {
                        Id = sub.Id,
                        Seq = sub.Seq + 1,
                        ErrorKind = ErrorKind.Cancelled,
                        Message = "Reset in progress.",
                        Final = true,
                    }
                )
            );
        }
        _subscriptions!.CancelAll();
        _cancelledIds.Clear();

        // Rebuild the evaluator.
        try
        {
            _evaluator!.Reset();
            HelperInjector.Inject(_evaluator, _host, _history!, _host.Config);
        }
        catch (Exception ex)
        {
            _host.LogError("[HotRepl] Evaluator reset failed.", ex);
            _clients!.SendTo(
                cmd.ConnectionId,
                MessageSerializer.Serialize(new ResetResultMessage { Id = cmd.Id, Success = false })
            );
            return;
        }

        _host.LogInfo("[HotRepl] Evaluator reset.");
        _clients!.SendTo(
            cmd.ConnectionId,
            MessageSerializer.Serialize(new ResetResultMessage { Id = cmd.Id, Success = true })
        );
    }

    private void HandleSelectEvaluator(SelectEvaluatorCmd cmd)
    {
        if (
            !_host.AvailableEvaluators.Any(e =>
                string.Equals(e.Name, cmd.Evaluator, StringComparison.Ordinal)
            )
        )
        {
            SendUnsupportedEvaluatorError(cmd);
            return;
        }

        CancelInflightForEvaluatorSwap();

        _evaluator?.Dispose();
        _evaluator = CreateEvaluator(cmd.Evaluator);
        _evaluatorReady = false;
        InitializeEvaluator();
        _evaluatorReady = true;

        _clients!.SendTo(
            cmd.ConnectionId,
            MessageSerializer.Serialize(
                new SelectEvaluatorResultMessage
                {
                    Id = cmd.Id,
                    Success = true,
                    Evaluator = _evaluator.Capabilities.Name,
                }
            )
        );

        _host.LogInfo($"[HotRepl] Evaluator selected: {_evaluator.Capabilities.Name}.");
    }

    private void SendUnsupportedEvaluatorError(SelectEvaluatorCmd cmd)
    {
        _clients!.SendTo(
            cmd.ConnectionId,
            MessageSerializer.Serialize(
                new SelectEvaluatorErrorMessage
                {
                    Id = cmd.Id,
                    ErrorKind = ErrorKind.Unsupported,
                    Message =
                        $"Evaluator '{cmd.Evaluator}' is not available. Available: "
                        + string.Join(", ", _host.AvailableEvaluators.Select(e => e.Name)),
                }
            )
        );
    }

    private void CancelInflightForEvaluatorSwap()
    {
        foreach (var sub in GetAllSubscriptions())
        {
            _clients!.SendTo(
                sub.ConnectionId,
                MessageSerializer.Serialize(
                    new SubscribeErrorMessage
                    {
                        Id = sub.Id,
                        Seq = sub.Seq + 1,
                        ErrorKind = ErrorKind.Cancelled,
                        Message = "Evaluator selection changed.",
                        Final = true,
                    }
                )
            );
        }
        _subscriptions!.CancelAll();

        while (_evalQueue.TryDequeue(out var job))
        {
            using (job)
            {
                _clients!.SendTo(
                    job.ConnectionId,
                    MessageSerializer.Serialize(
                        new EvalErrorMessage
                        {
                            Id = job.Id,
                            ErrorKind = ErrorKind.Cancelled,
                            Message = "Evaluator selection changed.",
                        }
                    )
                );
            }
        }
    }

    private void HandleHotReload()
    {
        var assembly = _evaluator!.PendingHotReloadAssembly;
        try
        {
            _evaluator!.Reset();
            HelperInjector.Inject(_evaluator, _host, _history!, _host.Config);
            _host.LogInfo("[HotRepl] Evaluator auto-reset after hot reload.");

            _clients!.Send(
                MessageSerializer.Serialize(
                    new AssemblyReloadMessage
                    {
                        Assembly = assembly,
                        Message = "Hot-reload detected. REPL session reset.",
                    }
                )
            );
        }
        catch (Exception ex)
        {
            _host.LogError("[HotRepl] Auto-reset after hot reload failed.", ex);
        }
    }

    private void HandleComplete(CompleteCmd cmd)
    {
        var result = _evaluator!.Complete(cmd.Code, cmd.CursorPos);
        _clients!.SendTo(
            cmd.ConnectionId,
            MessageSerializer.Serialize(
                new CompleteResultMessage
                {
                    Id = cmd.Id,
                    Completions = result.Completions,
                    DurationMs = result.DurationMs,
                }
            )
        );
    }

    private void HandleSubscribe(SubscribeCmd cmd)
    {
        var sub = new SubscriptionState(
            cmd.Id,
            cmd.ConnectionId,
            cmd.Code,
            cmd.IntervalFrames,
            cmd.OnChange,
            cmd.Limit,
            cmd.TimeoutMs
        );

        if (!_subscriptions!.TryAdd(sub, out var error))
        {
            _clients!.SendTo(
                cmd.ConnectionId,
                MessageSerializer.Serialize(
                    new SubscribeErrorMessage
                    {
                        Id = cmd.Id,
                        Seq = 0,
                        ErrorKind = ErrorKind.Runtime,
                        Message = error!,
                        Final = true,
                    }
                )
            );
        }
    }

    // ── Private: history and result sending ──────────────────────────────────

    private void RecordHistory(string code, EvalOutcome outcome)
    {
        try
        {
            string? serializedValue = null;
            if (outcome.Success && outcome.HasValue && outcome.Value != null)
            {
                serializedValue = JsonResultSerializer.Serialize(outcome.Value, _host.Config);
                serializedValue = JsonResultSerializer.Truncate(
                    serializedValue,
                    _host.Config.MaxResultLength
                );
            }
            Repl.__RecordEntry(code, serializedValue, outcome.ErrorMessage);
        }
        catch (ThreadAbortException)
        {
            // A stale watchdog can fire during history recording because _evalInProgress
            // is cleared before this call. Absorb it — history loss is non-fatal.
            Thread.ResetAbort();
        }
        catch
        {
            // History failure is always non-fatal.
        }
    }

    private void SendEvalOutcome(string id, Guid connectionId, EvalOutcome outcome)
    {
        string json;
        if (outcome.Success)
        {
            string? serialized = null;
            if (outcome.HasValue && outcome.Value != null)
            {
                serialized = JsonResultSerializer.Serialize(outcome.Value, _host.Config);
                serialized = JsonResultSerializer.Truncate(
                    serialized,
                    _host.Config.MaxResultLength
                );
            }
            json = MessageSerializer.Serialize(
                new EvalResultMessage
                {
                    Id = id,
                    HasValue = outcome.HasValue,
                    Value = serialized,
                    ValueType = outcome.ValueType,
                    Stdout = string.IsNullOrEmpty(outcome.Stdout) ? null : outcome.Stdout,
                    DurationMs = outcome.DurationMs,
                }
            );
        }
        else
        {
            var errorKind = outcome.ErrorKind ?? ErrorKind.Runtime;
            var errorMessage = outcome.ErrorMessage ?? "Unknown error.";

            if (string.Equals(errorKind, ErrorKind.Runtime, StringComparison.Ordinal))
                errorMessage = AppendCrossAssemblyHint(errorMessage);

            json = MessageSerializer.Serialize(
                new EvalErrorMessage
                {
                    Id = id,
                    ErrorKind = errorKind,
                    Message = errorMessage,
                    StackTrace = outcome.StackTrace,
                }
            );
        }
        _clients!.SendTo(connectionId, json);
    }

    // ── Private: client connection handling ───────────────────────────────────

    private void OnClientConnected(Guid id, IWebSocketConnection socket)
    {
        _clients!.OnConnected(id, socket);

        // Handshake can be sent immediately — its content is entirely statically
        // knowable and does not require the evaluator to be initialized.
        var usings = _host.AdditionalUsings.ToArray();
        var helpers = HelperInjector.AllHelperSignatures(_host);

        _clients.Send(
            MessageSerializer.Serialize(
                new HandshakeMessage
                {
                    Version = "1.0.0",
                    Evaluator = _evaluator?.Capabilities,
                    Host = _host.HostInfo,
                    AvailableEvaluators = _host.AvailableEvaluators.Select(e => e.Name).ToArray(),
                    CsharpVersion = _evaluator?.Capabilities.LanguageVersion ?? "unknown",
                    DefaultUsings = usings,
                    Helpers = helpers,
                    ControlPlane = _host.Config.ControlPlaneEnabled
                        ? new ControlPlaneHandshake
                        {
                            Supported = true,
                            ProtocolVersion = 1,
                            AuthRequired = _host.Config.RequireControlAuth,
                            LeaseRequired = _host.Config.RequireControlLease,
                            ArtifactRefsSupported = true,
                            JobEventsSupported = false,
                            JobEventReplaySupported = false,
                            Limits = new ControlPlaneLimits
                            {
                                MaxMessageBytes = _host.Config.MaxControlMessageBytes,
                                MaxInFlightCommands = 1,
                                MaxQueuedCommands = _host.Config.MaxQueuedControlCommands,
                                MaxJobEventBuffer = _host.Config.MaxJobEventBuffer,
                            },
                        }
                        : null,
                }
            )
        );
    }

    // ── Private: helpers ─────────────────────────────────────────────────────

    private System.Collections.Generic.IReadOnlyCollection<SubscriptionState> GetAllSubscriptions() =>
        _subscriptions!.GetAll();

    /// <summary>
    /// Detects known cross-assembly error patterns in runtime errors and appends a
    /// helpful suggestion. These occur after hot reloads when the evaluator holds
    /// references to types from a now-unloaded assembly.
    /// </summary>
    private static string AppendCrossAssemblyHint(string message)
    {
        const string hint =
            "\n\n[HotRepl] This error is likely caused by stale assembly references "
            + "after a hot reload. The evaluator should auto-reset on the next hot reload. "
            + "If the problem persists, try: eval reset";

        // FieldInfo.GetValue cross-assembly mismatch
        if (
            message.Contains("is not a field on the target object")
            || message.Contains("is not a field on the target type")
        )
            return message + hint;

        // InvalidCastException where source and target type names are identical
        // (e.g. "Cannot cast object of type 'Foo' to type 'Foo'")
        if (
            message.Contains("InvalidCastException")
            || message.StartsWith("Cannot cast object of type", StringComparison.Ordinal)
        )
        {
            // Look for the pattern: type 'X' to type 'X' — same name from different assemblies
            int firstQuote = message.IndexOf('\'');
            if (firstQuote >= 0)
            {
                int endFirst = message.IndexOf('\'', firstQuote + 1);
                if (endFirst > firstQuote)
                {
                    string firstName = message.Substring(firstQuote + 1, endFirst - firstQuote - 1);
                    int secondQuote = message.IndexOf('\'', endFirst + 1);
                    if (secondQuote >= 0)
                    {
                        int endSecond = message.IndexOf('\'', secondQuote + 1);
                        if (endSecond > secondQuote)
                        {
                            string secondName = message.Substring(
                                secondQuote + 1,
                                endSecond - secondQuote - 1
                            );
                            if (string.Equals(firstName, secondName, StringComparison.Ordinal))
                                return message + hint;
                        }
                    }
                }
            }
        }

        return message;
    }
}
