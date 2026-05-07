namespace HotRepl.Control.Artifacts;

/// <summary>Metadata reference to an artifact produced by a control-plane command or job.</summary>
public sealed record ArtifactRef(
    string LogicalName,
    string Uri,
    string? Path,
    string ContentType,
    long ByteSize,
    string Sha256,
    bool Finalized);
