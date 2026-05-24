using HotRepl.Control;

namespace HotRepl.UnityCommands;

internal readonly struct UnityCommandCatalogMetadataEntry
{
    public UnityCommandCatalogMetadataEntry(string name, ControlCommandKind kind, bool mutatesState)
    {
        Name = name;
        Kind = kind;
        MutatesState = mutatesState;
    }

    public string Name { get; }

    public ControlCommandKind Kind { get; }

    public bool MutatesState { get; }
}
