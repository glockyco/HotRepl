using System;

namespace HotRepl.Control;

/// <summary>Per-call metadata passed to a control-plane command handler.</summary>
public sealed record ControlCommandContext(string RequestId, TimeSpan? Timeout);
