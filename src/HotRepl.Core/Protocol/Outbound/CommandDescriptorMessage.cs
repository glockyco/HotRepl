using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Protocol;

internal sealed class CommandDescriptorMessage
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("version")]
    public int Version { get; set; }

    [JsonProperty("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonProperty("mutatesState")]
    public bool MutatesState { get; set; }

    [JsonProperty("argsSchema")]
    public JObject ArgsSchema { get; set; } = new();

    [JsonProperty("resultSchema")]
    public JObject ResultSchema { get; set; } = new();
}
