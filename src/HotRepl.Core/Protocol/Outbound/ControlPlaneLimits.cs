using Newtonsoft.Json;

namespace HotRepl.Protocol;

internal sealed class ControlPlaneLimits
{
    [JsonProperty("maxMessageBytes")]
    public int MaxMessageBytes { get; set; }

    [JsonProperty("maxInFlightCommands")]
    public int MaxInFlightCommands { get; set; }

    [JsonProperty("maxQueuedCommands")]
    public int MaxQueuedCommands { get; set; }

    [JsonProperty("maxJobEventBuffer")]
    public int MaxJobEventBuffer { get; set; }
}
