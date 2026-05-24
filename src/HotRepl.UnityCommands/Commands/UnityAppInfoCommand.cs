using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.UnityCommands.Models;

namespace HotRepl.UnityCommands.Commands;

/// <summary>Reports basic Unity application/runtime metadata.</summary>
public sealed class UnityAppInfoCommand : IControlCommandHandler<EmptyArgs, UnityAppInfo>
{
    /// <inheritdoc />
    public string Name => UnityCommandCatalogNames.AppInfo;

    /// <inheritdoc />
    public int Version => 1;

    /// <inheritdoc />
    public ControlCommandKind Kind => ControlCommandKind.Synchronous;

    /// <inheritdoc />
    public bool MutatesState => false;

    /// <inheritdoc />
    public ValueTask<ControlCommandResult<UnityAppInfo>> ExecuteAsync(
        ControlCommandContext context,
        EmptyArgs args,
        CancellationToken cancellationToken
    ) =>
        new(
            ControlCommandResult.Ok(
                new UnityAppInfo
                {
                    ProductName = UnityEngine.Application.productName,
                    UnityVersion = UnityEngine.Application.unityVersion,
                    Platform = UnityEngine.Application.platform.ToString(),
                    IsEditor = UnityEngine.Application.isEditor,
                }
            )
        );
}
