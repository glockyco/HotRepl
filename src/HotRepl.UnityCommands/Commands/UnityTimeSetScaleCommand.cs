using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.UnityCommands.Models;

namespace HotRepl.UnityCommands.Commands;

/// <summary>Sets UnityEngine.Time.timeScale.</summary>
public sealed class UnityTimeSetScaleCommand
    : IControlCommandHandler<UnitySetTimeScaleArgs, UnitySetTimeScaleResult>
{
    /// <inheritdoc />
    public string Name => UnityCommandCatalogMetadata.TimeSetScale.Name;

    /// <inheritdoc />
    public int Version => 1;

    /// <inheritdoc />
    public ControlCommandKind Kind => UnityCommandCatalogMetadata.TimeSetScale.Kind;

    /// <inheritdoc />
    public bool MutatesState => UnityCommandCatalogMetadata.TimeSetScale.MutatesState;

    /// <inheritdoc />
    public ValueTask<ControlCommandResult<UnitySetTimeScaleResult>> ExecuteAsync(
        ControlCommandContext<UnitySetTimeScaleResult> context,
        UnitySetTimeScaleArgs args,
        CancellationToken cancellationToken
    )
    {
        var previous = UnityEngine.Time.timeScale;
        UnityEngine.Time.timeScale = args.TimeScale;
        return new(
            ControlCommandResult.Ok(
                new UnitySetTimeScaleResult
                {
                    PreviousTimeScale = previous,
                    NewTimeScale = UnityEngine.Time.timeScale,
                }
            )
        );
    }
}
