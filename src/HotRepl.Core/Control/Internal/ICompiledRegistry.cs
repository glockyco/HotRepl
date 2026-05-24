namespace HotRepl.Control.Internal;

/// <summary>
/// Internal lookup used by the router. Implementations are paired with
/// the public <see cref="IControlCommandRegistry"/> on the same type.
/// </summary>
internal interface ICompiledRegistry
{
    /// <summary>Resolve a registered command to its compiled dispatch shape.</summary>
    bool TryGet(string name, out ICompiledControlCommand? handler);
}
