using System;

namespace HotRepl.Engine.Commands;

internal sealed record CompleteCmd(string Id, string Code, int CursorPos, Guid ConnectionId)
    : IEngineCommand;
