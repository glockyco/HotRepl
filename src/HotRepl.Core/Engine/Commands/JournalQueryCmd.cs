using System;

namespace HotRepl.Engine.Commands;

internal sealed record JournalQueryCmd(string Id, string? Kind, int? Limit, Guid ConnectionId)
    : IEngineCommand;
