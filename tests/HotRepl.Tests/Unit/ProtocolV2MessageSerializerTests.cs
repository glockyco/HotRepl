using System;
using HotRepl.Protocol;
using HotRepl.Protocol.Serialization;
using Xunit;

namespace HotRepl.Tests.Unit;

public class ProtocolV2MessageSerializerTests
{
    [Fact]
    public void Handshake_RoundTripsProtocolVersionAndEnforcedLimits()
    {
        var message = HandshakeMessage.CreateForTests();
        var json = ProtocolMessageSerializer.Serialize(message);
        var back = ProtocolMessageSerializer.Deserialize<HandshakeMessage>(json);

        Assert.Equal(MessageType.Handshake, back.Type);
        Assert.Equal(2, back.ProtocolVersion);
        Assert.Equal(4 * 1024 * 1024, back.Limits.MaxMessageBytes);
        Assert.Contains("maxJobConcurrency", back.Enforces);
    }

    [Fact]
    public void ParseType_RejectsMissingTypeAsInvalidRequest()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProtocolMessageSerializer.ParseType("{\"id\":\"missing-type\"}")
        );

        Assert.Contains("type", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorEnvelope_UsesClosedKindConstants()
    {
        var error = new HotReplErrorEnvelope(
            ErrorKind.ValidationFailed,
            "badArgs",
            "Arguments failed schema validation.",
            retryable: false,
            details: null
        );

        Assert.Equal("validation_failed", error.Kind);
    }
}
