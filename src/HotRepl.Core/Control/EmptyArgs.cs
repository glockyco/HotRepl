namespace HotRepl.Control;

/// <summary>
/// Marker type for typed commands that take no arguments. The schema
/// generator emits <c>{ "type": "object", "additionalProperties": false }</c>.
/// </summary>
public readonly struct EmptyArgs { }
