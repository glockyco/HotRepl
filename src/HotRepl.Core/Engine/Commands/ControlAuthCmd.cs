using System;

namespace HotRepl.Engine.Commands;

internal sealed record ControlAuthCmd(string Id, string? Token, Guid ConnectionId) : IEngineCommand;
