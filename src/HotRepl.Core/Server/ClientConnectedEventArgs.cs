using System;
using Fleck;

namespace HotRepl.Server;

/// <summary>Carries the connection id and underlying socket for a new client connection.</summary>
internal sealed class ClientConnectedEventArgs(Guid connectionId, IWebSocketConnection connection)
    : EventArgs
{
    public Guid ConnectionId { get; } = connectionId;
    public IWebSocketConnection Connection { get; } = connection;
}
