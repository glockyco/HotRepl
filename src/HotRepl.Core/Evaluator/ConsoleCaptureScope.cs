using System;
using System.Text;

namespace HotRepl.Evaluator;

/// <summary>
/// Console text collected for one evaluation. Collection stops when the scope is disposed, so text
/// that tasks started by the evaluation write later is not added.
/// </summary>
public sealed class ConsoleCaptureScope : IDisposable
{
    private readonly ConsoleCapture _owner;
    private readonly object _gate = new();
    private readonly StringBuilder _text = new();
    private bool _open = true;

    internal ConsoleCaptureScope(ConsoleCapture owner, ConsoleCaptureScope? outer)
    {
        _owner = owner;
        Outer = outer;
    }

    /// <summary>The collected text, or null when nothing was written.</summary>
    public string? Text
    {
        get
        {
            lock (_gate)
                return _text.Length > 0 ? _text.ToString() : null;
        }
    }

    internal ConsoleCaptureScope? Outer { get; }

    /// <summary>Stops collecting and restores the scope that was open before this one.</summary>
    public void Dispose() => _owner.End(this);

    internal void Append(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        lock (_gate)
        {
            if (_open)
                _text.Append(text);
        }
    }

    internal void Append(char value)
    {
        lock (_gate)
        {
            if (_open)
                _text.Append(value);
        }
    }

    internal void Append(char[] buffer, int index, int count)
    {
        lock (_gate)
        {
            if (_open)
                _text.Append(buffer, index, count);
        }
    }

    internal void Close()
    {
        lock (_gate)
            _open = false;
    }
}
