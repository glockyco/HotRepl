using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Text;
using HotRepl.Evaluation;
using HotRepl.Protocol;
using HotRepl.Serialization;
using HotRepl.Server;

namespace HotRepl.Hosting
{
    /// <summary>
    /// Composition root for the REPL system. Owns the WebSocket server and code
    /// evaluator, bridges incoming messages to the host's main thread via
    /// <see cref="Tick"/>, and enforces per-evaluation timeouts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The host creates a single <see cref="ReplEngine"/> at startup, calls
    /// <see cref="Start"/>, and invokes <see cref="Tick"/> once per frame from
    /// the main thread. The engine processes at most one evaluation per tick to
    /// avoid frame stalls.
    /// </para>
    /// <para>
    /// Thread safety: the concurrent queues accept messages from Fleck's thread
    /// pool. All compilation and execution happens on the thread that calls
    /// <see cref="Tick"/>.
    /// </para>
    /// </remarks>
    public sealed class ReplEngine : IDisposable
    {
        private readonly IReplHost _host;
        private readonly ReplConfig _config;

        // Created in Start(), torn down in Stop().
        private ReplServer? _server;
        private ICodeEvaluator? _evaluator;

        /// <summary>
        /// The thread that called <see cref="Start"/>. The watchdog timer uses
        /// this to abort a runaway evaluation.
        /// </summary>
        private Thread? _mainThread;

        // Inbound queues — written by Fleck threads, drained by Tick().
        private readonly ConcurrentQueue<QueuedEval> _evalQueue = new();
        private readonly ConcurrentQueue<string> _cancelQueue = new();
        private readonly ConcurrentQueue<QueuedCommand> _commandQueue = new();

        // Eval IDs that have been cancelled before they were dequeued.
        private readonly HashSet<string> _cancelledIds = new();

        // Active subscriptions — keyed by subscription id.
        private readonly Dictionary<string, ActiveSubscription> _subscriptions = new();
        private const int MaxSubscriptions = 8;
        private const int MaxConsecutiveErrors = 3;
        private int _frameCounter;

        /// <summary>
        /// Monotonically increasing generation counter for the current eval.
        /// Incremented at the start of each eval; the watchdog and cancel
        /// paths capture it and only abort if the generation still matches,
        /// preventing stale aborts from hitting the wrong eval.
        /// </summary>
        private long _evalGeneration;

        /// <summary>
        /// The message id of the evaluation currently executing inside
        /// <see cref="Tick"/>. Volatile so the watchdog timer can read it
        /// without a lock.
        /// </summary>
        private volatile string? _activeEvalId;

        /// <summary>
        /// Set by the cancel path (or the watchdog timer) to signal the
        /// currently executing evaluation should be treated as aborted.
        /// Checked after <see cref="Thread.ResetAbort"/>.
        /// </summary>
        private volatile bool _cancelRequested;

        private bool _disposed;

        /// <summary>Creates an engine but does not start it.</summary>
        /// <param name="host">Host environment providing assemblies, usings, and logging.</param>
        /// <param name="config">Optional configuration; defaults are used when null.</param>
        public ReplEngine(IReplHost host, ReplConfig? config = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _config = config ?? new ReplConfig();
        }

        /// <summary>
        /// Initializes the evaluator and starts the WebSocket server.
        /// Must be called from the host's main thread — the calling thread is
        /// captured for the watchdog timer.
        /// </summary>
        public void Start()
        {
            ThrowIfDisposed();
            if (_server != null)
                throw new InvalidOperationException("Engine is already running.");

            _mainThread = Thread.CurrentThread;

            _evaluator = CreateEvaluator();

            var handshake = MessageSerializer.Serialize(new HandshakeMessage
            {
                Version = "1.0.0",
                CsharpVersion = "7.x",
                DefaultUsings = _host.DefaultUsings.ToArray(),
                Helpers = Helpers.ReplHelpers.AdvertisedHelpers,
            });

            _server = new ReplServer(
                _config.Port,
                handshake,
                OnMessageReceived,
                msg => _host.Log(LogLevel.Debug, msg));

            _server.Start();

            _host.Log(LogLevel.Info, $"HotRepl engine started on port {_config.Port}.");
        }

        /// <summary>
        /// Processes at most one queued message per call. The host must call
        /// this once per frame from its main thread.
        /// </summary>
        public void Tick()
        {
            if (_evaluator == null || _server == null)
                return;

            // 1. Drain cancel queue — record for queued-eval filtering, remove subscriptions.
            while (_cancelQueue.TryDequeue(out var cancelId))
            {
                _cancelledIds.Add(cancelId);
                _subscriptions.Remove(cancelId);
            }

            // 2. Drain command queue (reset, ping).
            while (_commandQueue.TryDequeue(out var cmd))
            {
                HandleCommand(cmd);
            }

            // 3. Process one eval request (if any), skipping cancelled ones.
            while (_evalQueue.TryDequeue(out var request))
            {
                if (_cancelledIds.Remove(request.Id))
                {
                    _host.Log(LogLevel.Debug, $"Skipping cancelled eval '{request.Id}'.");
                    continue;
                }
                ExecuteEval(request);
                break; // At most one eval per tick.
            }

            // 4. Process subscriptions.
            _frameCounter++;
            ProcessSubscriptions();
        }

        /// <summary>Stops the WebSocket server and releases the evaluator.</summary>
        public void Stop()
        {
            if (_server != null)
            {
                _server.Dispose();
                _server = null;
            }

            if (_evaluator != null)
            {
                _evaluator.Dispose();
                _evaluator = null;
            }

            _mainThread = null;
            _host.Log(LogLevel.Info, "HotRepl engine stopped.");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Stop();
        }

        // ------------------------------------------------------------------
        // Message routing — called by ReplServer on Fleck's thread pool
        // ------------------------------------------------------------------

        private void OnMessageReceived(string messageType, string rawJson)
        {
            switch (messageType)
            {
                case "eval":
                    var eval = MessageSerializer.Deserialize<EvalRequest>(rawJson);
                    _evalQueue.Enqueue(new QueuedEval(
                        eval.Id,
                        eval.Code,
                        eval.TimeoutMs > 0 ? eval.TimeoutMs : _config.DefaultTimeoutMs));
                    break;

                case "cancel":
                    var cancel = MessageSerializer.Deserialize<CancelRequest>(rawJson);
                    EnqueueCancel(cancel.Id);
                    break;

                case "reset":
                    var reset = MessageSerializer.Deserialize<ResetRequest>(rawJson);
                    _commandQueue.Enqueue(new QueuedCommand(reset.Id, CommandKind.Reset));
                    break;

                case "ping":
                    var ping = MessageSerializer.Deserialize<PingRequest>(rawJson);
                    _commandQueue.Enqueue(new QueuedCommand(ping.Id, CommandKind.Ping));
                    break;

                case "complete":
                    var complete = MessageSerializer.Deserialize<CompleteRequest>(rawJson);
                    var code = complete.CursorPos >= 0 && complete.CursorPos < complete.Code.Length
                        ? complete.Code.Substring(0, complete.CursorPos)
                        : complete.Code;
                    _commandQueue.Enqueue(new QueuedCommand(complete.Id, CommandKind.Complete, code));
                    break;

                case "subscribe":
                    var sub = MessageSerializer.Deserialize<SubscribeRequest>(rawJson);
                    _commandQueue.Enqueue(new QueuedCommand(
                        sub.Id, CommandKind.Subscribe, rawJson));
                    break;

                default:
                    _host.Log(LogLevel.Warning, $"Unknown message type: {messageType}");
                    break;
            }
        }

        private void EnqueueCancel(string id)
        {
            _cancelQueue.Enqueue(id);

            // If the target is already running, abort its thread immediately.
            // Capture the generation to prevent aborting a different eval that
            // starts between our read and the actual abort.
            var gen = Interlocked.Read(ref _evalGeneration);
            if (_activeEvalId == id && _mainThread != null)
            {
                _cancelRequested = true;
                AbortMainThread(gen);
            }
        }

        // ------------------------------------------------------------------
        // Evaluation lifecycle
        // ------------------------------------------------------------------

        private void ExecuteEval(QueuedEval request)
        {
            var gen = Interlocked.Increment(ref _evalGeneration);
            _activeEvalId = request.Id;
            _cancelRequested = false;

            var sw = Stopwatch.StartNew();
            Timer? watchdog = null;
            EvalResult result;

            try
            {
                // Arm the watchdog — fires on the ThreadPool, aborts _mainThread.
                watchdog = new Timer(
                    _ => AbortMainThread(gen),
                    state: null,
                    dueTime: request.TimeoutMs,
                    period: Timeout.Infinite);

                result = _evaluator!.Evaluate(request.Code);
            }
            catch (ThreadAbortException)
            {
                Thread.ResetAbort();
                sw.Stop();
                result = EvalResult.Timeout(cancelled: _cancelRequested, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _host.Log(LogLevel.Error, $"Unexpected error during eval: {ex}");
                result = EvalResult.RuntimeError(ex.Message, ex.StackTrace, stdout: null, sw.ElapsedMilliseconds);
            }
            finally
            {
                // Clear _activeEvalId BEFORE disposing the watchdog to close the
                // race window where the watchdog fires between finally-entry and
                // the id-clear, aborting code outside ExecuteEval's handler.
                _activeEvalId = null;
                sw.Stop();
                watchdog?.Dispose();
            }

            RecordHistory(request.Code, result);
            SendResult(request.Id, result);
        }

        /// <summary>
        /// Records the eval result in the REPL-side HotRepl._history list.
        /// Uses Base64 encoding to avoid C# string-escaping issues.
        /// </summary>
        private void RecordHistory(string code, EvalResult result)
        {
            try
            {
                var codeB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(code));
                var valueB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                    result.HasValue && result.Value != null ? result.Value.ToString()! : ""));
                var errorB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(result.Error ?? ""));

                _evaluator?.Evaluate(
                    $"HotRepl._AddHistory(\"{codeB64}\", \"{valueB64}\", \"{errorB64}\");");
            }
            catch (Exception ex)
            {
                _host.Log(LogLevel.Debug, $"Failed to record history: {ex.Message}");
            }
        }


        private void SendResult(string id, EvalResult result)
        {
            try
            {
                string json;
                if (result.Success)
                {
                    var serializedValue = result.HasValue
                        ? ResultSerializer.Serialize(result.Value, _config.MaxEnumerableElements)
                        : null;

                    // Truncate oversized values to prevent flooding the wire.
                    if (serializedValue != null && serializedValue.Length > _config.MaxResultLength)
                        serializedValue = serializedValue.Substring(0, _config.MaxResultLength) + "…(truncated)";

                    json = MessageSerializer.Serialize(new EvalResultMessage
                    {
                        Id = id,
                        HasValue = result.HasValue,
                        Value = serializedValue,
                        ValueType = result.ValueType,
                        Stdout = result.Stdout,
                        DurationMs = result.DurationMs
                    });
                }
                else
                {
                    json = MessageSerializer.Serialize(new EvalErrorMessage
                    {
                        Id = id,
                        ErrorKind = result.ErrorKind ?? "runtime",
                        Message = result.Error ?? "Unknown error",
                        StackTrace = result.StackTrace
                    });
                }

                _server?.Send(json);
            }
            catch (Exception ex)
            {
                _host.Log(LogLevel.Error, $"Failed to send eval result: {ex.Message}");
            }
        }

        /// <summary>
        /// Aborts the main thread if the given generation still matches the
        /// current eval. This prevents a stale watchdog or cancel from
        /// aborting a subsequent, unrelated evaluation.
        /// </summary>
        private void AbortMainThread(long expectedGeneration)
        {
            var thread = _mainThread;
            if (thread != null
                && _activeEvalId != null
                && Interlocked.Read(ref _evalGeneration) == expectedGeneration)
            {
                _host.Log(LogLevel.Warning, $"Watchdog: aborting eval '{_activeEvalId}' (gen {expectedGeneration}).");
                thread.Abort();
            }
        }

        // ------------------------------------------------------------------
        // Commands
        // ------------------------------------------------------------------

        private void HandleCommand(QueuedCommand cmd)
        {
            switch (cmd.Kind)
            {
                case CommandKind.Reset:
                    _host.Log(LogLevel.Info, "Resetting evaluator.");
                    _evaluator?.Dispose();
                    _subscriptions.Clear();
                    _evaluator = CreateEvaluator();

                    var resetJson = MessageSerializer.Serialize(new ResetResultMessage
                    {
                        Id = cmd.Id,
                        Success = true
                    });
                    _server?.Send(resetJson);
                    break;

                case CommandKind.Ping:
                    var pongJson = MessageSerializer.Serialize(new PongMessage { Id = cmd.Id });
                    _server?.Send(pongJson);
                    break;

                case CommandKind.Complete:
                    var sw = Stopwatch.StartNew();
                    var completions = _evaluator?.GetCompletions(cmd.Data ?? "") ?? Array.Empty<string>();
                    sw.Stop();
                    var completeJson = MessageSerializer.Serialize(new CompleteResultMessage
                    {
                        Id = cmd.Id,
                        Completions = completions,
                        DurationMs = sw.ElapsedMilliseconds,
                    });
                    _server?.Send(completeJson);
                    break;

                case CommandKind.Subscribe:
                    if (cmd.Data != null)
                    {
                        var subReq = MessageSerializer.Deserialize<SubscribeRequest>(cmd.Data);
                        if (_subscriptions.Count >= MaxSubscriptions)
                        {
                            _server?.Send(MessageSerializer.Serialize(new SubscribeErrorMessage
                            {
                                Id = subReq.Id,
                                Seq = 0,
                                ErrorKind = "limit",
                                Message = $"Maximum {MaxSubscriptions} active subscriptions reached.",
                                Final = true,
                            }));
                        }
                        else
                        {
                            _subscriptions[subReq.Id] = new ActiveSubscription
                            {
                                Id = subReq.Id,
                                Code = subReq.Code,
                                IntervalFrames = Math.Max(1, subReq.IntervalFrames),
                                OnChange = subReq.OnChange,
                                Limit = subReq.Limit,
                                TimeoutMs = subReq.TimeoutMs > 0 ? subReq.TimeoutMs : _config.DefaultTimeoutMs,
                                LastEvalFrame = _frameCounter,
                            };
                            _host.Log(LogLevel.Debug,
                                $"Subscription '{subReq.Id}' created: interval={subReq.IntervalFrames} onChange={subReq.OnChange}");
                        }
                    }
                    break;

                default:
                    _host.Log(LogLevel.Warning, $"Unknown command kind: {cmd.Kind}");
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Subscriptions
        // ------------------------------------------------------------------

        private void ProcessSubscriptions()
        {
            if (_subscriptions.Count == 0 || _evaluator == null)
                return;

            // Collect ids to remove after iteration
            List<string>? toRemove = null;

            foreach (var kvp in _subscriptions)
            {
                var sub = kvp.Value;

                // Check interval
                if (_frameCounter - sub.LastEvalFrame < sub.IntervalFrames)
                    continue;

                sub.LastEvalFrame = _frameCounter;
                sub.Seq++;

                var result = _evaluator.Evaluate(sub.Code);

                if (result.Success)
                {
                    sub.ConsecutiveErrors = 0;
                    var serialized = result.HasValue
                        ? ResultSerializer.Serialize(result.Value, _config.MaxEnumerableElements)
                        : null;

                    // If onChange, skip if value unchanged
                    if (sub.OnChange && serialized == sub.LastValue)
                        continue;

                    sub.LastValue = serialized;

                    bool isFinal = sub.Limit > 0 && sub.Seq >= sub.Limit;

                    _server?.Send(MessageSerializer.Serialize(new SubscribeResultMessage
                    {
                        Id = sub.Id,
                        Seq = sub.Seq,
                        HasValue = result.HasValue,
                        Value = serialized,
                        ValueType = result.ValueType,
                        DurationMs = result.DurationMs,
                        Final = isFinal,
                    }));

                    if (isFinal)
                    {
                        (toRemove ??= new List<string>()).Add(sub.Id);
                    }
                }
                else
                {
                    sub.ConsecutiveErrors++;
                    bool isFinal = sub.ConsecutiveErrors >= MaxConsecutiveErrors;

                    _server?.Send(MessageSerializer.Serialize(new SubscribeErrorMessage
                    {
                        Id = sub.Id,
                        Seq = sub.Seq,
                        ErrorKind = result.ErrorKind ?? "runtime",
                        Message = result.Error ?? "Unknown error",
                        Final = isFinal,
                    }));

                    if (isFinal)
                    {
                        _host.Log(LogLevel.Warning,
                            $"Subscription '{sub.Id}' removed after {MaxConsecutiveErrors} consecutive errors.");
                        (toRemove ??= new List<string>()).Add(sub.Id);
                    }
                }
            }

            if (toRemove != null)
            {
                foreach (var id in toRemove)
                    _subscriptions.Remove(id);
            }
        }

        // ------------------------------------------------------------------
        // Evaluator factory
        // ------------------------------------------------------------------

        private ICodeEvaluator CreateEvaluator()
        {
            var eval = new MonoEvaluator(_host.ReferenceAssemblies, _host.DefaultUsings);

            // Inject helper library into the REPL environment
            var helperResult = eval.Evaluate(Helpers.ReplHelpers.Source);
            if (!helperResult.Success)
                _host.Log(LogLevel.Warning, $"Failed to inject helpers: {helperResult.Error}");

            return eval;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ReplEngine));
        }

        // ------------------------------------------------------------------
        // Internal message types — value types to avoid heap churn in queues
        // ------------------------------------------------------------------

        private readonly struct QueuedEval
        {
            public string Id { get; }
            public string Code { get; }
            public int TimeoutMs { get; }

            public QueuedEval(string id, string code, int timeoutMs)
            {
                Id = id;
                Code = code;
                TimeoutMs = timeoutMs;
            }
        }

        private readonly struct QueuedCommand
        {
            public string Id { get; }
            public CommandKind Kind { get; }
            public string? Data { get; }

            public QueuedCommand(string id, CommandKind kind, string? data = null)
            {
                Id = id;
                Kind = kind;
                Data = data;
            }
        }

        private enum CommandKind
        {
            Reset,
            Ping,
            Complete,
            Subscribe,
        }

        private sealed class ActiveSubscription
        {
            public string Id { get; set; } = "";
            public string Code { get; set; } = "";
            public int IntervalFrames { get; set; } = 1;
            public bool OnChange { get; set; }
            public int Limit { get; set; }  // 0 = unlimited
            public int TimeoutMs { get; set; } = 10000;
            public int Seq { get; set; }
            public int LastEvalFrame { get; set; }
            public string? LastValue { get; set; }
            public int ConsecutiveErrors { get; set; }
        }

    }
}
