using System;

namespace HotRepl.Server;

/// <summary>Carries the connection id of a client that disconnected or errored out.</summary>
internal sealed class ClientDisconnectedEventArgs(Guid connectionId) : EventArgs
{
    public Guid ConnectionId { get; } = connectionId;
}
