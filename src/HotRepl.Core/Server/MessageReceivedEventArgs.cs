using System;

namespace HotRepl.Server;

/// <summary>Carries the connection id and raw JSON payload of an inbound text frame.</summary>
internal sealed class MessageReceivedEventArgs(Guid connectionId, string rawJson) : EventArgs
{
    public Guid ConnectionId { get; } = connectionId;
    public string RawJson { get; } = rawJson;
}
