using System.Threading;
using System.Threading.Tasks;

namespace HotRepl.Control;

/// <summary>
/// Authoring-time interface for a typed control-plane command handler.
/// </summary>
/// <typeparam name="TArgs">
/// POCO argument type. Use <see cref="EmptyArgs"/> for commands that
/// take no arguments. Properties decorated with <c>[Required]</c>,
/// <c>[Range]</c>, <c>[Description]</c>, <c>[JsonProperty]</c> etc.
/// surface in the generated schema and are validated server-side
/// before the handler runs.
/// </typeparam>
/// <typeparam name="TOutput">
/// POCO output type. Same attribute rules apply; the schema surfaces
/// to clients via <c>command_describe</c>.
/// </typeparam>
public interface IControlCommandHandler<TArgs, TOutput>
{
    /// <summary>Stable wire name (e.g. <c>"compendium.info"</c>).</summary>
    string Name { get; }

    /// <summary>
    /// Wire-protocol major version. Bump when args or output shape
    /// changes incompatibly.
    /// </summary>
    int Version { get; }

    /// <summary>Synchronous (in-band response) or Job (out-of-band).</summary>
    ControlCommandKind Kind { get; }

    /// <summary>
    /// True if this command may change game/runtime state. Used by
    /// MCP to set the <c>destructiveHint</c> tool annotation.
    /// </summary>
    bool MutatesState { get; }

    /// <summary>
    /// Execute the command. Invoked on the host's main-thread execution
    /// path (via <c>ReplEngine.Tick()</c>). Continuations after
    /// <c>await</c> resume on the Unity main thread via the captured
    /// <see cref="System.Threading.SynchronizationContext"/>.
    /// </summary>
    ValueTask<ControlCommandResult<TOutput>> ExecuteAsync(
        ControlCommandContext<TOutput> context,
        TArgs args,
        CancellationToken cancellationToken
    );
}
