using System;

namespace HotRepl.Engine.Commands;

internal sealed record SubscribeCmd(
    string Id,
    string Code,
    int IntervalFrames,
    bool OnChange,
    int Limit,
    int TimeoutMs,
    Guid ConnectionId
) : IEngineCommand;
