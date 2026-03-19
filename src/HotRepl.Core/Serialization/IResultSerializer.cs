namespace HotRepl.Serialization;

/// <summary>
/// Converts arbitrary runtime values to JSON-safe strings.
/// Both methods are stateless and safe to call from any thread.
/// </summary>
internal interface IResultSerializer
{
    /// <summary>
    /// Serializes <paramref name="value"/> to a JSON string.
    /// MUST NOT throw — returns a JSON error object on serialization failure.
    /// Enumerables are capped at <see cref="ReplConfig.MaxEnumerableElements"/>.
    /// </summary>
    string Serialize(object? value, ReplConfig config);

    /// <summary>
    /// Returns <paramref name="serialized"/> unchanged if within <paramref name="maxLength"/>;
    /// otherwise returns the first <paramref name="maxLength"/> characters followed by a
    /// truncation marker that preserves the original length for diagnostic purposes.
    /// </summary>
    string Truncate(string serialized, int maxLength);
}
