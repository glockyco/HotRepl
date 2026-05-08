using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class ControlPlaneHandshake
{
    [JsonProperty("supported")]
    public bool Supported { get; set; }

    [JsonProperty("protocolVersion")]
    public int ProtocolVersion { get; set; }

    [JsonProperty("authRequired")]
    public bool AuthRequired { get; set; }

    [JsonProperty("leaseRequired")]
    public bool LeaseRequired { get; set; }

    [JsonProperty("artifactRefsSupported")]
    public bool ArtifactRefsSupported { get; set; }

    [JsonProperty("jobEventsSupported")]
    public bool JobEventsSupported { get; set; }

    [JsonProperty("limits")]
    public ControlPlaneLimits? Limits { get; set; }
}
