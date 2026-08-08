using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using Fleck;
using HotRepl.Control;
using HotRepl.Control.Jobs;
using HotRepl.Discovery;
using HotRepl.Engine.Commands;
using HotRepl.Evaluator;
using HotRepl.Helpers;
using HotRepl.Journal;
using HotRepl.Protocol;
using HotRepl.Protocol.Serialization;
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
///   2. Drain command queue — reset, complete, subscribe, typed commands
///   3. Start at most one queued control job
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
    private ControlJobManager? _controlJobs;
    private InstanceDocumentWriter? _instanceDocument;
    private ReplJournal? _journal;

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
        _journal = new ReplJournal(capacity: 1024);
        _evaluator = CreateEvaluator(_host.DefaultEvaluatorName);

        _wsServer = new ReplWebSocketServer(msg => _host.LogInfo(msg));
        _clients = new ClientRegistry(_wsServer, msg => _host.LogInfo(msg));
        _router = new MessageRouter(this, msg => _host.LogInfo(msg));
        _controlJobs = new ControlJobManager(
            _host.Config.MaxJobEventBuffer,
            _host.Config.MaxJobConcurrency
        );
        _controlRouter = new ControlCommandRouter(
            _host.ControlCommands,
            jobs: _controlJobs,
            config: _host.Config,
            onCommandResult: RecordCommandJournalEntry
        );

        _wsServer.ClientConnected += (_, e) => OnClientConnected(e.ConnectionId, e.Connection);
        _wsServer.ClientDisconnected += (_, e) => _clients.OnDisconnected(e.ConnectionId);
        _wsServer.MessageReceived += (_, e) => _router.HandleMessage(e.ConnectionId, e.RawJson);

        _wsServer.Start(_host.Config.Port, _host.Config.BindHost);
        _host.LogInfo($"[HotRepl] Engine started on port {_host.Config.Port}.");
        try
        {
            _instanceDocument = InstanceDocumentWriter.Write(_host.Config, _host.HostInfo);
        }
        catch (Exception ex)
        {
            _host.LogInfo($"[HotRepl] Failed to write instance discovery document: {ex.Message}");
        }
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

        // 3b. Cancel subscriptions targeted by a cancel request this Tick. (Eval
        //     cancels were already applied above; ids that match no subscription
        //     are ignored.)
        if (!_cancelledIds.IsEmpty)
        {
            foreach (var cancelledId in _cancelledIds.Keys)
                _subscriptions!.Cancel(cancelledId);
        }

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
                        Serialize(
                            EvalError(
                                job.Id,
                                ErrorKind.Cancelled,
                                "evalCancelled",
                                "Evaluation cancelled."
                            )
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
        _instanceDocument?.Dispose();
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

    internal int QueuedCommandCount => _commandQueue.Count + _evalQueue.Count;

    internal void SendProtocolError(Guid connectionId, string json) =>
        _clients?.SendControlTo(connectionId, json);

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
            _journal!.RecordEval(job.Id, outcome.Success, outcome.DurationMs, outcome.ErrorKind);
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
            case CompleteCmd c:
                HandleComplete(c);
                break;
            case SubscribeCmd s:
                HandleSubscribe(s);
                break;
            case SelectEvaluatorCmd s:
                HandleSelectEvaluator(s);
                break;
            case CommandsListCmd c:
                _clients!.SendControlTo(c.ConnectionId, Serialize(_controlRouter!.List(c.Id)));
                break;
            case CommandDescribeCmd c:
                _clients!.SendControlTo(
                    c.ConnectionId,
                    Serialize(
                        _controlRouter!.Describe(
                            new CommandDescribeMessage { Id = c.Id, Name = c.Name }
                        )
                    )
                );
                break;
            case CommandCallCmd c:
            {
                var response = _controlRouter!.Execute(c.Message, c.ConnectionId);
                _clients!.SendControlTo(c.ConnectionId, Serialize(response));
                if (response is JobAcceptedMessage accepted)
                    _jobRunQueue.Enqueue(accepted.JobId);
                break;
            }
            case JobStatusCmd c:
                HandleJobStatus(c);
                break;
            case JobCancelCmd c:
                HandleJobCancel(c);
                break;
            case JournalQueryCmd c:
                HandleJournalQuery(c);
                break;
        }
    }

    private void HandleJobStatus(JobStatusCmd cmd)
    {
        var response = _controlRouter!.GetJobStatus(cmd.Message, cmd.ConnectionId);
        _clients!.SendControlTo(cmd.ConnectionId, Serialize(response));
    }

    private void HandleJobCancel(JobCancelCmd cmd)
    {
        var response = _controlRouter!.CancelJob(cmd.Message, cmd.ConnectionId);
        _clients!.SendControlTo(cmd.ConnectionId, Serialize(response));
    }

    private void HandleJournalQuery(JournalQueryCmd cmd)
    {
        var limit = cmd.Limit.GetValueOrDefault(100);
        var entries = _journal!.Query(cmd.Kind, limit).Select(ToMessage).ToArray();
        _clients!.SendControlTo(
            cmd.ConnectionId,
            Serialize(new JournalQueryResultMessage { Id = cmd.Id, Entries = entries })
        );
    }

    private void HandleReset(ResetCmd cmd)
    {
        CancelPendingEvalsForReset();
        CancelSubscriptionsForReset();
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
                Serialize(new ResetResultMessage { Id = cmd.Id, Success = false })
            );
            return;
        }

        _journal!.RecordReset(cmd.Id);
        _host.LogInfo("[HotRepl] Evaluator reset.");
        _clients!.SendTo(
            cmd.ConnectionId,
            Serialize(new ResetResultMessage { Id = cmd.Id, Success = true })
        );
    }

    private void CancelPendingEvalsForReset()
    {
        while (_evalQueue.TryDequeue(out var job))
        {
            using (job)
            {
                _clients!.SendTo(
                    job.ConnectionId,
                    Serialize(
                        EvalError(
                            job.Id,
                            ErrorKind.Cancelled,
                            "evalCancelled",
                            "Reset in progress."
                        )
                    )
                );
            }
        }
    }

    private void CancelSubscriptionsForReset()
    {
        foreach (var sub in GetAllSubscriptions())
        {
            _clients!.SendTo(
                sub.ConnectionId,
                Serialize(
                    SubscriptionError(
                        sub.Id,
                        sub.Seq + 1,
                        ErrorKind.Cancelled,
                        "evalCancelled",
                        "Reset in progress.",
                        final: true
                    )
                )
            );
        }

        _subscriptions!.CancelAll();
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
            Serialize(
                new
                {
                    type = "select_evaluator_result",
                    id = cmd.Id,
                    success = true,
                    evaluator = _evaluator.Capabilities.Name,
                }
            )
        );

        _host.LogInfo($"[HotRepl] Evaluator selected: {_evaluator.Capabilities.Name}.");
    }

    private void SendUnsupportedEvaluatorError(SelectEvaluatorCmd cmd)
    {
        _clients!.SendTo(
            cmd.ConnectionId,
            Serialize(
                new
                {
                    type = "select_evaluator_error",
                    id = cmd.Id,
                    error = Error(
                        ErrorKind.UnsupportedOperation,
                        "unsupportedEvaluator",
                        $"Evaluator '{cmd.Evaluator}' is not available. Available: "
                            + string.Join(", ", _host.AvailableEvaluators.Select(e => e.Name)),
                        retryable: false
                    ),
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
                Serialize(
                    SubscriptionError(
                        sub.Id,
                        sub.Seq + 1,
                        ErrorKind.Cancelled,
                        "evalCancelled",
                        "Evaluator selection changed.",
                        final: true
                    )
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
                    Serialize(
                        EvalError(
                            job.Id,
                            ErrorKind.Cancelled,
                            "evalCancelled",
                            "Evaluator selection changed."
                        )
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
                Serialize(
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
            Serialize(
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
                Serialize(
                    SubscriptionError(
                        cmd.Id,
                        0,
                        ErrorKind.Internal,
                        "subscriptionRejected",
                        error!,
                        final: true
                    )
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

    private void RecordCommandJournalEntry(ControlCommandJournalEntry entry) =>
        _journal!.RecordCommand(
            entry.Id,
            entry.Name,
            entry.Success,
            durationMs: 0,
            errorKind: entry.ErrorKind
        );

    private static JournalEntry ToMessage(ReplJournalEntry entry) =>
        new()
        {
            Id = entry.Id,
            Kind = entry.Kind,
            Name = entry.Name,
            Success = entry.Success,
            DurationMs = entry.DurationMs,
            ErrorKind = entry.ErrorKind,
            Timestamp = entry.Timestamp.ToString("O", CultureInfo.InvariantCulture),
        };

    private void SendEvalOutcome(string id, Guid connectionId, EvalOutcome outcome)
    {
        string json;
        if (outcome.Success)
        {
            var wire =
                outcome.HasValue && outcome.Value != null
                    ? JsonResultSerializer.ToWireValue(outcome.Value, _host.Config)
                    : default;
            json = Serialize(
                new EvalResultMessage
                {
                    Id = id,
                    HasValue = outcome.HasValue,
                    Value = wire.Value,
                    Truncated = wire.Truncated,
                    TruncatedBytes = wire.ByteCount,
                    ValueType = outcome.ValueType,
                    Stdout = string.IsNullOrEmpty(outcome.Stdout) ? null : outcome.Stdout,
                    DurationMs = outcome.DurationMs,
                }
            );
        }
        else
        {
            var errorKind = outcome.ErrorKind ?? ErrorKind.Internal;
            var errorMessage = outcome.ErrorMessage ?? "Unknown error.";

            if (string.Equals(errorKind, ErrorKind.Internal, StringComparison.Ordinal))
                errorMessage = AppendCrossAssemblyHint(errorMessage);

            json = Serialize(EvalError(id, errorKind, ToEvalErrorCode(errorKind), errorMessage));
        }
        _clients!.SendTo(connectionId, json);
    }

    // ── Private: client connection handling ───────────────────────────────────

    private void OnClientConnected(Guid id, IWebSocketConnection socket)
    {
        _clients!.OnConnected(id, socket);

        // Handshake can be sent immediately — its content is statically knowable
        // from host and evaluator capabilities.
        var usings = _host.AdditionalUsings.ToArray();
        var helpers = HelperInjector.AllHelperSignatures(_host);
        var json = RuntimeHandshakeFactory.Serialize(
            _host.Config,
            _host.HostInfo,
            _evaluator!.Capabilities,
            _host.AvailableEvaluators.Select(e => e.Name).ToArray(),
            usings,
            helpers
        );

        _clients.SendTo(id, json);
    }

    // ── Private: helpers ─────────────────────────────────────────────────────

    private System.Collections.Generic.IReadOnlyCollection<SubscriptionState> GetAllSubscriptions() =>
        _subscriptions!.GetAll();

    private static string Serialize(object message) => ProtocolMessageSerializer.Serialize(message);

    private static HotReplErrorEnvelope Error(
        string kind,
        string code,
        string message,
        bool retryable
    ) => new(kind, code, message, retryable, details: null);

    private static string ToEvalErrorCode(string kind) =>
        kind switch
        {
            ErrorKind.ValidationFailed => "compileError",
            ErrorKind.Timeout => "evalTimeout",
            ErrorKind.Cancelled => "evalCancelled",
            _ => "runtimeException",
        };

    private static EvalErrorMessage EvalError(
        string id,
        string kind,
        string code,
        string message,
        bool retryable = false
    ) => new() { Id = id, Error = Error(kind, code, message, retryable) };

    private static SubscribeErrorMessage SubscriptionError(
        string id,
        long seq,
        string kind,
        string code,
        string message,
        bool final
    ) =>
        new()
        {
            Id = id,
            Seq = seq,
            Error = Error(kind, code, message, retryable: false),
            Final = final,
        };

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
            message.Contains("is not a field on the target object", StringComparison.Ordinal)
            || message.Contains("is not a field on the target type", StringComparison.Ordinal)
        )
            return message + hint;

        // InvalidCastException where source and target type names are identical
        // (e.g. "Cannot cast object of type 'Foo' to type 'Foo'")
        if (
            message.Contains("InvalidCastException", StringComparison.Ordinal)
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
