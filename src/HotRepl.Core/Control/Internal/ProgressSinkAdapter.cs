using System;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Internal;

/// <summary>
/// Bridges the job manager's <c>Action&lt;JObject?, string?&gt;</c>
/// progress callback onto <c>IProgress&lt;ControlCommandProgress&gt;</c>
/// for handlers.
/// </summary>
internal sealed class ProgressSinkAdapter : IProgress<ControlCommandProgress>
{
    private readonly Action<JObject?, string?> _sink;

    public ProgressSinkAdapter(Action<JObject?, string?> sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public void Report(ControlCommandProgress value) => _sink(value.Snapshot, value.Message);
}
