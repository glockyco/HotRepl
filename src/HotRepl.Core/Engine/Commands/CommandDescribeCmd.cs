using System;

namespace HotRepl.Engine.Commands;

internal sealed record CommandDescribeCmd(string Id, string Name, Guid ConnectionId) : IEngineCommand;
