using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace HotRepl.Evaluator;

/// <summary>How an evaluator can enforce timeout and cancellation requests.</summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum TimeoutMode
{
    /// <summary>The engine can abort the main thread and recover the REPL session.</summary>
    HardAbort,

    /// <summary>The evaluator observes a cancellation token; runtime preemption is best effort.</summary>
    Cooperative,

    /// <summary>The evaluator cannot preempt execution.</summary>
    None,
}
