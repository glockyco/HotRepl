using Newtonsoft.Json;

namespace HotRepl.Evaluator;

/// <summary>Capability metadata reported by concrete evaluator implementations.</summary>
public sealed class EvaluatorCapabilities
{
    [JsonProperty("name")]
    public string Name { get; init; } = string.Empty;

    [JsonProperty("languageVersion")]
    public string LanguageVersion { get; init; } = string.Empty;

    [JsonProperty("supportsPersistentState")]
    public bool SupportsPersistentState { get; init; }

    [JsonProperty("supportsCompletion")]
    public bool SupportsCompletion { get; init; }

    [JsonProperty("timeoutMode")]
    public TimeoutMode TimeoutMode { get; init; }
}
