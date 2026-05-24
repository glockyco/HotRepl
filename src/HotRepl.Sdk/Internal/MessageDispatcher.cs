using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Sdk.Internal;

/// <summary>Receive loop and request/response correlation for one HotRepl session.</summary>
internal sealed class MessageDispatcher : IAsyncDisposable
{
    private readonly IDuplexFrameChannel _channel;
    private readonly ConcurrentDictionary<string, PendingRequest> _pending = new(
        StringComparer.Ordinal
    );
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _readLoop;

    public MessageDispatcher(IDuplexFrameChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _readLoop = Task.Run(ReadLoopAsync);
    }

    public event EventHandler<PushedMessageEventArgs>? Pushed;

    public Task<JObject> ExpectResponseAsync(
        string id,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Request id is required.", nameof(id));
        }

        var completion = new TaskCompletionSource<JObject>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token
        );
        timeoutSource.CancelAfter(timeout);
        var pending = new PendingRequest(completion, timeoutSource);
        if (!_pending.TryAdd(id, pending))
        {
            pending.Dispose();
            throw new InvalidOperationException($"Request id '{id}' is already pending.");
        }

        timeoutSource.Token.Register(() => CancelPending(id, timeoutSource.Token));
        return completion.Task;
    }

    public Task SendAsync(JObject message, CancellationToken cancellationToken)
    {
        var json = message.ToString(Formatting.None);
        return _channel.SendAsync(json, cancellationToken);
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                var raw = await _channel.ReceiveAsync(_shutdown.Token).ConfigureAwait(false);
                if (raw is null)
                {
                    break;
                }

                var message = JObject.Parse(raw);
                var id = message["id"]?.ToString();
                if (id is not null && _pending.TryRemove(id, out var pending))
                {
                    pending.Dispose();
                    pending.Completion.TrySetResult(message);
                }
                else
                {
                    Pushed?.Invoke(this, new PushedMessageEventArgs(message));
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex)
        {
            FailAllPending(new HotReplConnectionException("Read loop terminated.", ex));
        }
    }

    private void CancelPending(string id, CancellationToken cancellationToken)
    {
        if (_pending.TryRemove(id, out var pending))
        {
            pending.Dispose();
            pending.Completion.TrySetCanceled(cancellationToken);
        }
    }

    private void FailAllPending(HotReplException error)
    {
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var pending))
            {
                pending.Dispose();
                pending.Completion.TrySetException(error);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        try
        {
            await _readLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }

        FailAllPending(new HotReplConnectionException("Session closed."));
        await _channel.DisposeAsync().ConfigureAwait(false);
        _shutdown.Dispose();
    }
}
