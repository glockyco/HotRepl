using System;

namespace HotRepl.Engine.Commands;

internal sealed record CommandDescribeCmd(string Id, Guid ConnectionId) : IEngineCommand;
