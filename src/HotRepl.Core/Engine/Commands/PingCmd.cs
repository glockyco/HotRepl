using System;

namespace HotRepl.Engine.Commands;

internal sealed record PingCmd(string Id, Guid ConnectionId) : IEngineCommand;
