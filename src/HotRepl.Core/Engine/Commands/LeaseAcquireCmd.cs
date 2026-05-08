using System;

namespace HotRepl.Engine.Commands;

internal sealed record LeaseAcquireCmd(
    string Id,
    string SessionId,
    string ClientName,
    Guid ConnectionId
) : IEngineCommand;
