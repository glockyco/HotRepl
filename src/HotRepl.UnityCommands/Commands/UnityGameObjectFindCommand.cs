using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.UnityCommands.Models;

namespace HotRepl.UnityCommands.Commands;

/// <summary>Finds a Unity GameObject and returns a JSON-friendly snapshot.</summary>
public sealed class UnityGameObjectFindCommand
    : IControlCommandHandler<UnityGameObjectFindArgs, UnityGameObjectFindResult>
{
    /// <inheritdoc />
    public string Name => UnityCommandCatalogMetadata.GameObjectFind.Name;

    /// <inheritdoc />
    public int Version => 1;

    /// <inheritdoc />
    public ControlCommandKind Kind => UnityCommandCatalogMetadata.GameObjectFind.Kind;

    /// <inheritdoc />
    public bool MutatesState => UnityCommandCatalogMetadata.GameObjectFind.MutatesState;

    /// <inheritdoc />
    public ValueTask<ControlCommandResult<UnityGameObjectFindResult>> ExecuteAsync(
        ControlCommandContext<UnityGameObjectFindResult> context,
        UnityGameObjectFindArgs args,
        CancellationToken cancellationToken
    )
    {
        var gameObject = UnityEngine.GameObject.Find(args.Path);
        return new(
            ControlCommandResult.Ok(
                new UnityGameObjectFindResult
                {
                    GameObject = gameObject is null ? null : ToDto(gameObject),
                }
            )
        );
    }

    private static UnityGameObject ToDto(UnityEngine.GameObject gameObject)
    {
        var components = gameObject.GetComponents<UnityEngine.Component>();
        var componentTypeNames = new string[components.Length];
        for (var i = 0; i < components.Length; i++)
        {
            componentTypeNames[i] = components[i]?.GetType().FullName ?? "<null>";
        }

        return new UnityGameObject
        {
            Name = gameObject.name,
            ActiveInHierarchy = gameObject.activeInHierarchy,
            Layer = gameObject.layer,
            Tag = gameObject.tag,
            Position = Vec3.From(gameObject.transform.position),
            ComponentTypeNames = componentTypeNames,
        };
    }
}
