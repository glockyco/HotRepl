using System;

namespace HotRepl.Engine.Commands;

internal sealed record ResetCmd(string Id, Guid ConnectionId) : IEngineCommand;
