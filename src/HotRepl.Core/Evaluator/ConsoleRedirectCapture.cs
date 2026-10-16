using System;
using System.IO;
using System.Text;
using System.Threading;

namespace HotRepl.Evaluator;

/// <summary>
/// Collects console output by installing a forwarding writer as <see cref="Console.Out"/> for the
/// lifetime of the capture. Every write still reaches the writer that the capture replaced.
/// </summary>
/// <remarks>
/// Use this capture only where <see cref="Console.SetOut"/> replaces <see cref="Console.Out"/>.
/// BepInEx 5 intercepts SetOut by default: its own writer stays the console writer and forwards to
/// the writer passed to SetOut. Forwarding to the replaced writer would then loop back through the
/// interceptor, so the constructor detects interception and throws instead.
/// </remarks>
public sealed class ConsoleRedirectCapture : ConsoleCapture
{
    private readonly Func<TextWriter> _getOut;
    private readonly Action<TextWriter> _setOut;
    private readonly TextWriter _replaced;
    private readonly TextWriter _installed;

    /// <summary>Installs the capture on <see cref="Console.Out"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Console.SetOut"/> does not replace <see cref="Console.Out"/> in this runtime.
    /// </exception>
    public ConsoleRedirectCapture()
        : this(() => Console.Out, Console.SetOut) { }

    internal ConsoleRedirectCapture(Func<TextWriter> getOut, Action<TextWriter> setOut)
    {
        _getOut = getOut;
        _setOut = setOut;
        _replaced = getOut();

        var writer = new ForwardingWriter(this, _replaced);
        setOut(writer);
        _installed = getOut();
        if (ReferenceEquals(_installed, _replaced))
        {
            // The interceptor now forwards to the writer. Forwarding further would reach the
            // interceptor again, and the writer it forwarded to before is unknown.
            writer.Detach();
            throw new InvalidOperationException(
                "Console.SetOut did not replace Console.Out, so the runtime intercepts it. "
                    + "A host on this runtime must collect console output from the interceptor."
            );
        }

        writer.ForwardTo(_replaced);
    }

    /// <summary>
    /// Restores the replaced writer while the capture is still the console writer. A component that
    /// replaced the capture's writer since may forward to it, so the writer then keeps forwarding.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && ReferenceEquals(_getOut(), _installed))
            _setOut(_replaced);
        base.Dispose(disposing);
    }

    private sealed class ForwardingWriter : TextWriter
    {
        private readonly ConsoleRedirectCapture _owner;
        private readonly TextWriter _replaced;
        private readonly object _gate = new();

        // Writes that arrive between installation and its confirmation wait here, so neither a
        // replacing nor an intercepting SetOut loses or loops them.
        private StringBuilder? _pending = new();
        private TextWriter? _target;

        public ForwardingWriter(ConsoleRedirectCapture owner, TextWriter replaced)
            : base(replaced.FormatProvider)
        {
            _owner = owner;
            _replaced = replaced;
        }

        public override Encoding Encoding => _replaced.Encoding;

        public void ForwardTo(TextWriter target)
        {
            lock (_gate)
            {
                if (_pending is { Length: > 0 })
                    target.Write(_pending.ToString());
                _pending = null;
                Volatile.Write(ref _target, target);
            }
        }

        public void Detach()
        {
            lock (_gate)
            {
                _pending = null;
                Volatile.Write(ref _target, Null);
            }
        }

        public override void Write(char value)
        {
            _owner.Collect(value);
            var target = Volatile.Read(ref _target);
            if (target is null)
            {
                lock (_gate)
                {
                    target = _target;
                    if (target is null)
                    {
                        _pending?.Append(value);
                        return;
                    }
                }
            }
            target.Write(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            _owner.Collect(buffer, index, count);
            var target = Volatile.Read(ref _target);
            if (target is null)
            {
                lock (_gate)
                {
                    target = _target;
                    if (target is null)
                    {
                        _pending?.Append(buffer, index, count);
                        return;
                    }
                }
            }
            target.Write(buffer, index, count);
        }

        public override void Write(string? value)
        {
            _owner.Collect(value);
            var target = Volatile.Read(ref _target);
            if (target is null)
            {
                lock (_gate)
                {
                    target = _target;
                    if (target is null)
                    {
                        _pending?.Append(value);
                        return;
                    }
                }
            }
            target.Write(value);
        }

        public override void Flush() => Volatile.Read(ref _target)?.Flush();
    }
}
