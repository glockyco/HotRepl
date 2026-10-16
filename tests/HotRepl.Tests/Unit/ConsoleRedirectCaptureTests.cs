using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Evaluator;
using Xunit;

namespace HotRepl.Tests.Unit;

public sealed class ConsoleRedirectCaptureTests
{
    [Fact]
    public void Begin_CollectsWritesOfItsFlowWhileForwardingThem()
    {
        using var downstream = new StringWriter();
        var console = new FakeConsole(downstream);
        using var capture = console.Capture();

        string? collected;
        using (var scope = capture.Begin())
        {
            console.Out.Write("a");
            console.Out.Write('b');
            console.Out.WriteLine(42);
            collected = scope.Text;
        }

        var expected = "ab42" + Environment.NewLine;
        Assert.Equal(expected, collected);
        Assert.Equal(expected, downstream.ToString());
    }

    [Fact]
    public async Task Begin_CollectsWritesOfTasksThatItsFlowStarts()
    {
        using var downstream = new StringWriter();
        var console = new FakeConsole(downstream);
        using var capture = console.Capture();
        using var scope = capture.Begin();

        await Task.Run(() => console.Out.Write("task"));

        Assert.Equal("task", scope.Text);
    }

    [Fact]
    public void Begin_IgnoresWritesOfUnrelatedThreads()
    {
        using var downstream = new StringWriter();
        var console = new FakeConsole(downstream);
        using var capture = console.Capture();
        using var write = new ManualResetEventSlim();
        using var written = new ManualResetEventSlim();
        // Started before the scope opens, like a game thread, so its flow carries no scope.
        var thread = new Thread(() =>
        {
            if (!write.Wait(TimeSpan.FromSeconds(10)))
                return;
            console.Out.Write("other");
            written.Set();
        })
        {
            IsBackground = true,
        };
        thread.Start();

        using (var scope = capture.Begin())
        {
            write.Set();
            Assert.True(written.Wait(TimeSpan.FromSeconds(10)));
            Assert.Null(scope.Text);
        }

        thread.Join();
        Assert.Equal("other", downstream.ToString());
    }

    [Fact]
    public void DisposedScope_StopsCollecting()
    {
        using var downstream = new StringWriter();
        var console = new FakeConsole(downstream);
        using var capture = console.Capture();

        var scope = capture.Begin();
        console.Out.Write("in");
        scope.Dispose();
        console.Out.Write("out");

        Assert.Equal("in", scope.Text);
        Assert.Equal("inout", downstream.ToString());
    }

    [Fact]
    public void Dispose_RestoresTheReplacedWriter()
    {
        using var downstream = new StringWriter();
        var console = new FakeConsole(downstream);

        console.Capture().Dispose();

        Assert.Same(downstream, console.Out);
    }

    [Fact]
    public void Dispose_KeepsALaterRedirectForwarding()
    {
        using var downstream = new StringWriter();
        var console = new FakeConsole(downstream);
        var capture = console.Capture();
        using var later = new RelayWriter(console.Out);
        console.Out = later;

        capture.Dispose();
        console.Out.Write("x");

        Assert.Same(later, console.Out);
        Assert.Equal("x", downstream.ToString());
    }

    [Fact]
    public void Constructor_RejectsSetOutThatRedirectsAnInterceptor()
    {
        // BepInEx 5 keeps its own writer as Console.Out, and SetOut changes where it forwards.
        using var downstream = new StringWriter();
        using var interceptor = new InterceptingWriter(downstream);

        Assert.Throws<InvalidOperationException>(() =>
            new ConsoleRedirectCapture(() => interceptor, writer => interceptor.Next = writer)
        );
        interceptor.WriteLine("after");

        Assert.Equal("after", Assert.Single(interceptor.Logged));
    }

    private sealed class FakeConsole
    {
        public FakeConsole(TextWriter initial) => Out = initial;

        public TextWriter Out { get; set; }

        public ConsoleRedirectCapture Capture() => new(() => Out, writer => Out = writer);
    }

    private sealed class RelayWriter : TextWriter
    {
        private readonly TextWriter _next;

        public RelayWriter(TextWriter next) => _next = next;

        public override Encoding Encoding => _next.Encoding;

        public override void Write(char value) => _next.Write(value);

        public override void Write(string? value) => _next.Write(value);
    }

    private sealed class InterceptingWriter : TextWriter
    {
        public InterceptingWriter(TextWriter next) => Next = next;

        public TextWriter Next { get; set; }

        public List<string?> Logged { get; } = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(string? value)
        {
            Logged.Add(value);
            Next.Write(value);
        }

        public override void WriteLine(string? value)
        {
            Logged.Add(value);
            Next.WriteLine(value);
        }
    }
}
