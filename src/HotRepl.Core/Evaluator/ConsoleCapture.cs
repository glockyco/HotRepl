using System;
using System.Threading;

namespace HotRepl.Evaluator;

/// <summary>
/// Collects what evaluations write to the console while console output keeps reaching its usual
/// destination. The engine obtains one capture from <see cref="IReplHost.CreateConsoleCapture"/>
/// when it starts, opens a scope around every evaluation, and disposes the capture when it stops.
/// </summary>
public abstract class ConsoleCapture : IDisposable
{
    private readonly AsyncLocal<ConsoleCaptureScope?> _scope = new();
    private bool _disposed;

    /// <summary>
    /// Starts collecting the console text that the calling execution flow writes, including tasks
    /// and threads that the flow starts, until the returned scope is disposed. Text that unrelated
    /// threads write meanwhile is not collected.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The capture was disposed.</exception>
    public ConsoleCaptureScope Begin()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        var scope = new ConsoleCaptureScope(this, _scope.Value);
        _scope.Value = scope;
        return scope;
    }

    /// <summary>Releases the console hook that feeds this capture.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    internal void End(ConsoleCaptureScope scope)
    {
        scope.Close();
        if (ReferenceEquals(_scope.Value, scope))
            _scope.Value = scope.Outer;
    }

    /// <summary>True when the current execution flow has an open scope.</summary>
    protected bool IsCollecting => _scope.Value is not null;

    /// <summary>Adds text that the current execution flow wrote to the console to its scope.</summary>
    protected void Collect(string? text) => _scope.Value?.Append(text);

    /// <summary>Adds a character that the current execution flow wrote to the console to its scope.</summary>
    protected void Collect(char value) => _scope.Value?.Append(value);

    /// <summary>Adds characters that the current execution flow wrote to the console to its scope.</summary>
    protected void Collect(char[] buffer, int index, int count) =>
        _scope.Value?.Append(buffer, index, count);

    /// <summary>Removes the console hook. Called once, from <see cref="Dispose()"/>.</summary>
    protected virtual void Dispose(bool disposing) { }
}
