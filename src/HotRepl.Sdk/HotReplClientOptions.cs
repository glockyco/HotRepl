using System;
using Newtonsoft.Json;

namespace HotRepl.Sdk;

/// <summary>Connection and request defaults for a HotRepl client.</summary>
public sealed class HotReplClientOptions
{
    /// <summary>Maximum time allowed for the WebSocket connection and handshake.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Maximum time allowed for a request/response round-trip.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Default polling interval for job progress and completion.</summary>
    public TimeSpan JobPollingInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Opt-in client-side schema validation switch for future SDK validation.</summary>
    public bool ValidateSchemas { get; set; }

    /// <summary>Serializer settings used for typed request and response payloads.</summary>
    public JsonSerializerSettings? SerializerSettings { get; set; }
}
