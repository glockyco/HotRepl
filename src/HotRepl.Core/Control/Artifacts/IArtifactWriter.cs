using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HotRepl.Control.Artifacts;

/// <summary>
/// Authoring-time surface for attaching artifacts to a command result.
/// Handlers receive this writer via the command context and attach
/// top-level artifact references by logical name.
/// </summary>
/// <remarks>
/// Two attachments with the same <c>logicalName</c> within one handler
/// invocation: the second replaces the first.
/// </remarks>
public interface IArtifactWriter
{
    /// <summary>Attach an in-memory byte buffer under <paramref name="logicalName"/>.</summary>
    ValueTask<ArtifactRef> AttachBytesAsync(
        string logicalName,
        ReadOnlyMemory<byte> data,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default
    );

    /// <summary>Attach the contents of <paramref name="stream"/> under <paramref name="logicalName"/>.</summary>
    ValueTask<ArtifactRef> AttachStreamAsync(
        string logicalName,
        Stream stream,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default
    );

    /// <summary>Attach an existing file under <paramref name="logicalName"/>.</summary>
    ValueTask<ArtifactRef> AttachFileAsync(
        string logicalName,
        string path,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default
    );
}
