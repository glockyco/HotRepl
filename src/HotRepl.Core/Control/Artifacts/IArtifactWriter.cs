using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HotRepl.Control.Artifacts;

/// <summary>
/// Writes binary artifacts produced by a control-command handler.
/// Implementations live in <c>HotRepl.Core</c> and route to the host's
/// configured artifact store (typically a temp directory under the
/// game's plugin folder). Handlers retrieve the writer via
/// <c>ControlCommandContext.Artifacts</c>; the returned
/// <see cref="ArtifactRef"/> goes into
/// <c>ControlCommandResult&lt;TOutput&gt;.Artifacts</c>.
/// </summary>
/// <remarks>
/// Two writes with the same <c>logicalName</c> within one handler
/// invocation: the second replaces the first.
/// </remarks>
public interface IArtifactWriter
{
    /// <summary>Persist the given byte buffer under <paramref name="logicalName"/>.</summary>
    ValueTask<ArtifactRef> WriteAsync(
        string logicalName,
        ReadOnlyMemory<byte> bytes,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default
    );

    /// <summary>Persist the stream contents under <paramref name="logicalName"/>.</summary>
    ValueTask<ArtifactRef> WriteStreamAsync(
        string logicalName,
        Stream stream,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default
    );
}
