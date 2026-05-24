using Newtonsoft.Json.Linq;

namespace HotRepl.Control;

/// <summary>Progress payload for a job command.</summary>
public sealed record ControlCommandProgress(JObject? Snapshot = null, string? Message = null);
