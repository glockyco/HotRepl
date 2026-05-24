using Newtonsoft.Json.Linq;

namespace HotRepl.Sdk;

/// <summary>Runtime capabilities advertised by the HotRepl handshake.</summary>
public sealed class HotReplCapabilities
{
    /// <summary>Create a capabilities snapshot.</summary>
    public HotReplCapabilities(JObject raw, int protocolVersion, bool schemaValidation)
    {
        Raw = raw;
        ProtocolVersion = protocolVersion;
        SchemaValidation = schemaValidation;
    }

    /// <summary>Raw handshake payload.</summary>
    public JObject Raw { get; }

    /// <summary>Wire protocol version.</summary>
    public int ProtocolVersion { get; }

    /// <summary>True when the runtime validates command args server-side.</summary>
    public bool SchemaValidation { get; }
}
