using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.UnityCommands.Models;

namespace HotRepl.UnityCommands.Commands;

/// <summary>Reports basic Unity application/runtime metadata.</summary>
public sealed class UnityAppInfoCommand : IControlCommandHandler<EmptyArgs, UnityAppInfo>
{
    /// <inheritdoc />
    public string Name => UnityCommandCatalogMetadata.AppInfo.Name;

    /// <inheritdoc />
    public int Version => 1;

    /// <inheritdoc />
    public ControlCommandKind Kind => UnityCommandCatalogMetadata.AppInfo.Kind;

    /// <inheritdoc />
    public bool MutatesState => UnityCommandCatalogMetadata.AppInfo.MutatesState;

    /// <inheritdoc />
    public ValueTask<ControlCommandResult<UnityAppInfo>> ExecuteAsync(
        ControlCommandContext<UnityAppInfo> context,
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
