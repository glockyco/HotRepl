using System;

namespace HotRepl.Engine.Commands;

internal sealed record CommandsListCmd(string Id, string? Since, Guid ConnectionId)
    : IEngineCommand;
