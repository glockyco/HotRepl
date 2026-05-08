using System;

namespace HotRepl.Engine.Commands;

/// <summary>Marker interface for commands queued by MessageRouter for Tick() processing.</summary>
internal interface IEngineCommand
{
    string Id { get; }
    Guid ConnectionId { get; }
}
