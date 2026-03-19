using System;
using HotRepl.Protocol;
using Newtonsoft.Json;
using Xunit;

namespace HotRepl.Tests.Unit;

public class MessageSerializerTests
{
    // ── ParseType ─────────────────────────────────────────────────────────────

    [Fact]
    public void ParseType_ExtractsType()
    {
        var type = MessageSerializer.ParseType("{\"type\":\"eval\",\"id\":\"1\"}");
        Assert.Equal("eval", type);
    }

    [Fact]
    public void ParseType_MissingType_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            MessageSerializer.ParseType("{\"id\":\"1\"}"));
    }

    [Fact]
    public void ParseType_InvalidJson_Throws()
    {
        Assert.ThrowsAny<JsonException>(() =>
            MessageSerializer.ParseType("not json"));
    }

    // ── Inbound round-trips ───────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_EvalMessage()
    {
        var msg = new EvalMessage { Id = "t-1", Code = "1+1", TimeoutMs = 5000 };
        var json = MessageSerializer.Serialize(msg);
        var back = MessageSerializer.Deserialize<EvalMessage>(json);

        Assert.Equal(MessageType.Eval, back.Type);
        Assert.Equal("t-1", back.Id);
        Assert.Equal("1+1", back.Code);
        Assert.Equal(5000, back.TimeoutMs);
    }

    [Fact]
    public void RoundTrip_CompleteMessage()
    {
        var msg = new CompleteMessage { Id = "t-6", Code = "Console.", CursorPos = 8 };
        var json = MessageSerializer.Serialize(msg);
        var back = MessageSerializer.Deserialize<CompleteMessage>(json);

        Assert.Equal(MessageType.Complete, back.Type);
        Assert.Equal("Console.", back.Code);
        Assert.Equal(8, back.CursorPos);
    }

    [Fact]
    public void RoundTrip_SubscribeMessage()
    {
        var msg = new SubscribeMessage
        {
            Id = "t-8",
            Code = "Time.time",
            IntervalFrames = 60,
            OnChange = true,
            Limit = 10,
            TimeoutMs = 3000,
        };
        var json = MessageSerializer.Serialize(msg);
        var back = MessageSerializer.Deserialize<SubscribeMessage>(json);

        Assert.Equal(MessageType.Subscribe, back.Type);
        Assert.Equal("Time.time", back.Code);
        Assert.Equal(60, back.IntervalFrames);
        Assert.True(back.OnChange);
        Assert.Equal(10, back.Limit);
        Assert.Equal(3000, back.TimeoutMs);
    }

    // ── Outbound round-trips ──────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_HandshakeMessage()
    {
        var msg = new HandshakeMessage
        {
            Version = "1.0.0",
            CsharpVersion = "7.3",
            DefaultUsings = new[] { "System", "System.Linq" },
            Helpers = new[] { "dump" },
        };
        var json = MessageSerializer.Serialize(msg);
        var back = MessageSerializer.Deserialize<HandshakeMessage>(json);

        Assert.Equal("1.0.0", back.Version);
        Assert.Equal("7.3", back.CsharpVersion);
        Assert.Equal(new[] { "System", "System.Linq" }, back.DefaultUsings);
        Assert.Equal(new[] { "dump" }, back.Helpers);
    }

    [Fact]
    public void RoundTrip_EvalResultMessage()
    {
        var msg = new EvalResultMessage
        {
            Id = "t-2",
            HasValue = true,
            Value = "42",
            ValueType = "System.Int32",
            Stdout = null,
            DurationMs = 7,
        };
        var json = MessageSerializer.Serialize(msg);
        var back = MessageSerializer.Deserialize<EvalResultMessage>(json);

        Assert.Equal("t-2", back.Id);
        Assert.True(back.HasValue);
        Assert.Equal("42", back.Value);
        Assert.Equal("System.Int32", back.ValueType);
        Assert.Null(back.Stdout);
    }

    [Fact]
    public void RoundTrip_EvalErrorMessage()
    {
        var msg = new EvalErrorMessage
        {
            Id = "t-3",
            ErrorKind = "runtime",
            Message = "NullRef",
            StackTrace = null,
        };
        var json = MessageSerializer.Serialize(msg);
        var back = MessageSerializer.Deserialize<EvalErrorMessage>(json);

        Assert.Equal("runtime", back.ErrorKind);
        Assert.Equal("NullRef", back.Message);
        Assert.Null(back.StackTrace);
    }

    [Fact]
    public void RoundTrip_ResetResultMessage()
    {
        var msg = new ResetResultMessage { Id = "t-4", Success = true };
        var json = MessageSerializer.Serialize(msg);
        var back = MessageSerializer.Deserialize<ResetResultMessage>(json);

        Assert.Equal("t-4", back.Id);
        Assert.True(back.Success);
    }

    [Fact]
    public void RoundTrip_PongMessage()
    {
        var msg = new PongMessage { Id = "t-5" };
        var json = MessageSerializer.Serialize(msg);
        var back = MessageSerializer.Deserialize<PongMessage>(json);

        Assert.Equal(MessageType.Pong, back.Type);
        Assert.Equal("t-5", back.Id);
    }

    [Fact]
    public void RoundTrip_CompleteResultMessage()
    {
        var msg = new CompleteResultMessage
        {
            Id = "t-7",
            Completions = new[] { "WriteLine", "Write" },
            DurationMs = 12,
        };
        var json = MessageSerializer.Serialize(msg);
        var back = MessageSerializer.Deserialize<CompleteResultMessage>(json);

        Assert.Equal(new[] { "WriteLine", "Write" }, back.Completions);
        Assert.Equal(12, back.DurationMs);
    }

    [Fact]
    public void RoundTrip_SubscribeResultMessage()
    {
        var msg = new SubscribeResultMessage
        {
            Id = "t-9",
            Seq = 3,
            HasValue = true,
            Value = "1.5",
            ValueType = "System.Single",
            DurationMs = 1,
            Final = true,
        };
        var json = MessageSerializer.Serialize(msg);
        var back = MessageSerializer.Deserialize<SubscribeResultMessage>(json);

        Assert.Equal(3, back.Seq);
        Assert.True(back.HasValue);
        Assert.Equal("1.5", back.Value);
        Assert.True(back.Final);
    }

    [Fact]
    public void RoundTrip_SubscribeErrorMessage()
    {
        var msg = new SubscribeErrorMessage
        {
            Id = "t-10",
            Seq = 1,
            ErrorKind = "runtime",
            Message = "boom",
            Final = false,
        };
        var json = MessageSerializer.Serialize(msg);
        var back = MessageSerializer.Deserialize<SubscribeErrorMessage>(json);

        Assert.Equal(1, back.Seq);
        Assert.Equal("runtime", back.ErrorKind);
        Assert.Equal("boom", back.Message);
        Assert.False(back.Final);
    }

    // ── Serialization properties ──────────────────────────────────────────────

    [Fact]
    public void Serialize_OmitsNullProperties()
    {
        // Null Value and Stdout must be absent — not emitted as "value":null.
        // This keeps payloads minimal and lets clients use presence checks.
        var msg = new EvalResultMessage { Id = "t-11", Value = null };
        var json = MessageSerializer.Serialize(msg);

        Assert.DoesNotContain("\"value\"", json);
        Assert.DoesNotContain("\"stdout\"", json);
    }

    [Fact]
    public void Serialize_UsesCamelCaseFieldNames()
    {
        var msg = new EvalResultMessage { Id = "t-12", HasValue = true };
        var json = MessageSerializer.Serialize(msg);

        Assert.Contains("\"hasValue\"", json);
        Assert.DoesNotContain("\"HasValue\"", json);
    }
}
