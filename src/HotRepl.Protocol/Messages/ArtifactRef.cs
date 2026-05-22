using Newtonsoft.Json;

namespace HotRepl.Protocol;

/// <summary>Reference metadata for an artifact produced by a command or job.</summary>
public sealed record ArtifactRef
{
    [JsonProperty("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonProperty("path")]
    public string? Path { get; set; }

    [JsonProperty("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonProperty("byteSize")]
    public long ByteSize { get; set; }

    [JsonProperty("contentType")]
    public string ContentType { get; set; } = string.Empty;

    [JsonProperty("finalized")]
    public bool Finalized { get; set; }
}
