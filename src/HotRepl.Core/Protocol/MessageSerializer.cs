using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace HotRepl.Protocol
{
    /// <summary>
    /// Handles JSON serialization/deserialization of protocol messages
    /// using camelCase property naming.
    /// </summary>
    internal static class MessageSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None
        };

        /// <summary>
        /// Extracts the "type" field from a raw JSON message without full deserialization.
        /// </summary>
        /// <exception cref="InvalidOperationException">JSON has no "type" field.</exception>
        public static string ParseType(string json)
        {
            var obj = JObject.Parse(json);
            var token = obj["type"];
            if (token == null)
                throw new InvalidOperationException("Message JSON missing required \"type\" field.");
            return token.Value<string>()!;
        }

        /// <summary>Deserializes a JSON string into a typed message.</summary>
        public static T Deserialize<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, Settings)
                ?? throw new InvalidOperationException($"Deserialization of {typeof(T).Name} returned null.");
        }

        /// <summary>Serializes a message object to a JSON string.</summary>
        public static string Serialize(object message)
        {
            return JsonConvert.SerializeObject(message, Settings);
        }
    }
}
