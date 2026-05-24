using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Sdk.Internal;

namespace HotRepl.Testing.Tests;

internal sealed class FakeFrameChannel : IDuplexFrameChannel
{
    private readonly Queue<string?> _incoming = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly SemaphoreSlim _sentSignal = new(0);
    private bool _disposed;

    public List<string> Sent { get; } = new();

    public void EnqueueIncoming(string? json)
    {
        _incoming.Enqueue(json);
        _signal.Release();
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        await _signal.WaitAsync(cancellationToken);
        return _incoming.Dequeue();
    }

    public Task SendAsync(string json, CancellationToken cancellationToken)
    {
        Sent.Add(json);
        _sentSignal.Release();
        return Task.CompletedTask;
    }

    public async Task WaitForSentCountAsync(int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (Sent.Count < count)
        {
            await _sentSignal.WaitAsync(timeout.Token);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _signal.Dispose();
            _sentSignal.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
