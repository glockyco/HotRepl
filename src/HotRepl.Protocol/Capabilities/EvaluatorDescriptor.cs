using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Evaluator capabilities advertised in the v2 handshake.</summary>
public sealed record EvaluatorDescriptor
{
    /// <summary>Evaluator name.</summary>
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>C# language version supported by the evaluator.</summary>
    [JsonProperty("languageVersion")]
    public string LanguageVersion { get; set; } = string.Empty;

    /// <summary>Whether eval state persists across requests.</summary>
    [JsonProperty("persistentState")]
    public bool PersistentState { get; set; }

    /// <summary>Whether completion is supported.</summary>
    [JsonProperty("supportsCompletion")]
    public bool SupportsCompletion { get; set; }

    /// <summary>Runtime cancellation mode: cooperative, hardAbort, or unsupported.</summary>
    [JsonProperty("cancellation")]
    public string Cancellation { get; set; } = "unsupported";
}
