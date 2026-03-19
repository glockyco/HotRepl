using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Fleck;
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
///   Main thread    → Start / Tick / Dispose
///   Watchdog timer → may call Thread.Abort on the main thread
///
/// Tick() drain order (invariant):
///   1. Process cancel requests — populate _cancelledIds, abort if matching eval running
///   2. Drain command queue — reset, ping, complete, subscribe
///   3. Execute at most one eval
///   4. Tick subscriptions
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
    private IResultSerializer? _serializer;
    private HistoryTracker? _history;

    // ── Queues — written by Fleck threads, drained by Tick() ──────────────────
    private readonly ConcurrentQueue<EvalJob> _evalQueue = new();
    private readonly ConcurrentQueue<IEngineCommand> _commandQueue = new();

    // Cancel: populated by Fleck threads via CancelEval(); checked by Tick().
    // ConcurrentDictionary used as a concurrent set (value is ignored).
    private readonly ConcurrentDictionary<string, bool> _cancelledIds = new();

    // ── Watchdog state — protected by _abortLock ─────────────────────────────
    private readonly object _abortLock = new();
    private Thread? _mainThread;
    private bool _evalInProgress;
    private string? _currentEvalId;
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
        _serializer = new JsonResultSerializer();
        _subscriptions = new SubscriptionManager(_host.Config);
        _evaluator = new MonoCSharpEvaluator(_host);

        _wsServer = new ReplWebSocketServer(msg => _host.LogInfo(msg));
        _clients = new ClientRegistry(_wsServer, msg => _host.LogInfo(msg));
        _router = new MessageRouter(this, msg => _host.LogInfo(msg));

        _wsServer.ClientConnected += OnClientConnected;
        _wsServer.ClientDisconnected += _clients.OnDisconnected;
        _wsServer.MessageReceived += _router.HandleMessage;

        _wsServer.Start(_host.Config.Port);
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

        // 3. At most one eval per Tick.
        while (_evalQueue.TryDequeue(out var job))
        {
            if (_cancelledIds.TryRemove(job.Id, out _))
            {
                // Cancelled before it ever ran.
                _clients!.SendTo(job.ConnectionId, MessageSerializer.Serialize(new EvalErrorMessage
                {
                    Id = job.Id,
                    ErrorKind = ErrorKind.Cancelled,
                    Message = "Evaluation cancelled.",
                }));
                continue; // try next in queue — but only process one non-cancelled eval
            }
            ExecuteEval(job);
            break;
        }

        // 4. Subscriptions.
        _subscriptions!.Tick(
            (id, code, timeoutMs) => GuardedEvaluate(id, code, timeoutMs),
            (connId, json) => _clients!.SendTo(connId, json),
            _serializer!);
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
            if (_evalInProgress && _currentEvalId == id)
                _mainThread?.Abort();
        }
    }

    internal void EnqueueCommand(IEngineCommand cmd) => _commandQueue.Enqueue(cmd);

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
        EvalOutcome outcome = RunGuarded(job.Id, job.Code, job.TimeoutMs);
        RecordHistory(job.Code, outcome);
        SendEvalOutcome(job.Id, job.ConnectionId, outcome);
    }

    /// <summary>
    /// Core execution primitive reused for both regular evals and subscriptions.
    /// Sets up the watchdog, calls the evaluator, and resolves the Aborted sentinel
    /// (returned by Evaluate() after it catches ThreadAbortException and calls ResetAbort)
    /// into either Timeout or Cancelled based on watchdog state.
    /// </summary>
    private EvalOutcome GuardedEvaluate(string id, string code, int timeoutMs)
        => RunGuarded(id, code, timeoutMs);

    private EvalOutcome RunGuarded(string id, string code, int timeoutMs)
    {
        long gen;
        lock (_abortLock)
        {
            _evalInProgress = true;
            _currentEvalId = id;
            gen = ++_currentGeneration;
        }
        _timedOut = false;

        var sw = Stopwatch.StartNew();
        Timer? watchdog = null;

        try
        {
            watchdog = new Timer(_ =>
            {
                lock (_abortLock)
                {
                    if (_evalInProgress && _currentGeneration == gen)
                    {
                        _timedOut = true;
                        _mainThread?.Abort();
                    }
                }
            }, null, timeoutMs, Timeout.Infinite);

            var outcome = _evaluator!.Evaluate(code);

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
            // Dispose watchdog before clearing _evalInProgress so a late-firing
            // timer can't race with a subsequent eval's setup.
            watchdog?.Dispose();
            lock (_abortLock)
            {
                _evalInProgress = false;
                _currentEvalId = null;
            }
        }
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
                _clients!.SendTo(p.ConnectionId, MessageSerializer.Serialize(new PongMessage { Id = p.Id }));
                break;
            case CompleteCmd c:
                HandleComplete(c);
                break;
            case SubscribeCmd s:
                HandleSubscribe(s);
                break;
        }
    }

    private void HandleReset(ResetCmd cmd)
    {
        // Drain and cancel all pending evals.
        while (_evalQueue.TryDequeue(out var job))
        {
            _clients!.SendTo(job.ConnectionId, MessageSerializer.Serialize(new EvalErrorMessage
            {
                Id = job.Id,
                ErrorKind = ErrorKind.Cancelled,
                Message = "Reset in progress.",
            }));
        }

        // Cancel all subscriptions with a final error.
        foreach (var sub in GetAllSubscriptions())
        {
            _clients!.SendTo(sub.ConnectionId, MessageSerializer.Serialize(new SubscribeErrorMessage
            {
                Id = sub.Id,
                Seq = sub.Seq + 1,
                ErrorKind = ErrorKind.Cancelled,
                Message = "Reset in progress.",
                Final = true,
            }));
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
            _clients!.SendTo(cmd.ConnectionId, MessageSerializer.Serialize(new ResetResultMessage
            {
                Id = cmd.Id,
                Success = false,
            }));
            return;
        }

        _host.LogInfo("[HotRepl] Evaluator reset.");
        _clients!.SendTo(cmd.ConnectionId, MessageSerializer.Serialize(new ResetResultMessage
        {
            Id = cmd.Id,
            Success = true,
        }));
    }

    private void HandleComplete(CompleteCmd cmd)
    {
        var result = _evaluator!.Complete(cmd.Code, cmd.CursorPos);
        _clients!.SendTo(cmd.ConnectionId, MessageSerializer.Serialize(new CompleteResultMessage
        {
            Id = cmd.Id,
            Completions = result.Completions,
            DurationMs = result.DurationMs,
        }));
    }

    private void HandleSubscribe(SubscribeCmd cmd)
    {
        var sub = new SubscriptionState(
            cmd.Id, cmd.ConnectionId, cmd.Code,
            cmd.IntervalFrames, cmd.OnChange, cmd.Limit, cmd.TimeoutMs);

        if (!_subscriptions!.TryAdd(sub, out var error))
        {
            _clients!.SendTo(cmd.ConnectionId, MessageSerializer.Serialize(new SubscribeErrorMessage
            {
                Id = cmd.Id,
                Seq = 0,
                ErrorKind = ErrorKind.Runtime,
                Message = error!,
                Final = true,
            }));
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
                serializedValue = _serializer!.Serialize(outcome.Value, _host.Config);
                serializedValue = _serializer.Truncate(serializedValue, _host.Config.MaxResultLength);
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
                serialized = _serializer!.Serialize(outcome.Value, _host.Config);
                serialized = _serializer.Truncate(serialized, _host.Config.MaxResultLength);
            }
            json = MessageSerializer.Serialize(new EvalResultMessage
            {
                Id = id,
                HasValue = outcome.HasValue,
                Value = serialized,
                ValueType = outcome.ValueType,
                Stdout = string.IsNullOrEmpty(outcome.Stdout) ? null : outcome.Stdout,
                DurationMs = outcome.DurationMs,
            });
        }
        else
        {
            json = MessageSerializer.Serialize(new EvalErrorMessage
            {
                Id = id,
                ErrorKind = outcome.ErrorKind ?? ErrorKind.Runtime,
                Message = outcome.ErrorMessage ?? "Unknown error.",
                StackTrace = outcome.StackTrace,
            });
        }
        _clients!.SendTo(connectionId, json);
    }

    // ── Private: client connection handling ───────────────────────────────────

    private void OnClientConnected(Guid id, IWebSocketConnection socket)
    {
        _clients!.OnConnected(id, socket);

        // Handshake can be sent immediately — its content is entirely statically
        // knowable and does not require the evaluator to be initialized.
        var usings = MonoCSharpEvaluator.DefaultUsings.Concat(_host.AdditionalUsings).ToArray();
        var helpers = HelperInjector.AllHelperSignatures(_host);

        _clients.Send(MessageSerializer.Serialize(new HandshakeMessage
        {
            Version = "1.0.0",
            CsharpVersion = "7.x",
            DefaultUsings = usings,
            Helpers = helpers,
        }));
    }

    // ── Private: helpers ─────────────────────────────────────────────────────

    private System.Collections.Generic.IReadOnlyCollection<SubscriptionState> GetAllSubscriptions()
        => _subscriptions!.GetAll();
}

// ── Engine command types (internal to this file) ──────────────────────────────

/// <summary>Marker interface for commands queued by MessageRouter for Tick() processing.</summary>
internal interface IEngineCommand
{
    string Id { get; }
    Guid ConnectionId { get; }
}

internal sealed class ResetCmd : IEngineCommand
{
    public string Id { get; }
    public Guid ConnectionId { get; }
    public ResetCmd(string id, Guid connectionId) { Id = id; ConnectionId = connectionId; }
}

internal sealed class PingCmd : IEngineCommand
{
    public string Id { get; }
    public Guid ConnectionId { get; }
    public PingCmd(string id, Guid connectionId) { Id = id; ConnectionId = connectionId; }
}

internal sealed class CompleteCmd : IEngineCommand
{
    public string Id { get; }
    public string Code { get; }
    public int CursorPos { get; }
    public Guid ConnectionId { get; }
    public CompleteCmd(string id, string code, int cursorPos, Guid connectionId)
    { Id = id; Code = code; CursorPos = cursorPos; ConnectionId = connectionId; }
}

internal sealed class SubscribeCmd : IEngineCommand
{
    public string Id { get; }
    public string Code { get; }
    public int IntervalFrames { get; }
    public bool OnChange { get; }
    public int Limit { get; }
    public int TimeoutMs { get; }
    public Guid ConnectionId { get; }
    public SubscribeCmd(string id, string code, int intervalFrames, bool onChange, int limit, int timeoutMs, Guid connectionId)
    { Id = id; Code = code; IntervalFrames = intervalFrames; OnChange = onChange; Limit = limit; TimeoutMs = timeoutMs; ConnectionId = connectionId; }
}
