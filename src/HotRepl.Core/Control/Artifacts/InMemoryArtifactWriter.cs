using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HotRepl.Control.Artifacts;

/// <summary>
/// In-memory <see cref="IArtifactWriter"/>. Suitable for the default
/// command-dispatch path: artifacts live for the duration of the
/// containing command invocation (or job lifetime), then are
/// released when the writer is no longer reachable.
/// <see cref="Snapshot"/> returns the current state for adapter
/// projection.
/// </summary>
public sealed class InMemoryArtifactWriter : IArtifactWriter
{
    private readonly object _sync = new();
    private readonly Dictionary<string, StoredArtifact> _store = new(StringComparer.Ordinal);
    private readonly string _uriPrefix;

    /// <summary>Create a writer that stamps each <see cref="ArtifactRef.Uri"/> with the given prefix.</summary>
    public InMemoryArtifactWriter(string uriPrefix = "hotrepl-artifact://memory/")
    {
        _uriPrefix = uriPrefix;
    }

    /// <inheritdoc />
    public ValueTask<ArtifactRef> WriteAsync(
        string logicalName,
        ReadOnlyMemory<byte> bytes,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(logicalName))
        {
            throw new ArgumentException("Logical name required.", nameof(logicalName));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var copy = bytes.ToArray();
        var artifact = new ArtifactRef(
            LogicalName: logicalName,
            Uri: _uriPrefix + logicalName,
            Path: null,
            ContentType: contentType,
            ByteSize: copy.Length,
            Sha256: Sha256Hex(copy),
            Finalized: true
        );

        lock (_sync)
        {
            _store[logicalName] = new StoredArtifact(artifact, copy);
        }

        return new ValueTask<ArtifactRef>(artifact);
    }

    /// <inheritdoc />
    public async ValueTask<ArtifactRef> WriteStreamAsync(
        string logicalName,
        Stream stream,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default
    )
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, 81920, cancellationToken).ConfigureAwait(false);
        return await WriteAsync(logicalName, ms.ToArray(), contentType, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Snapshot of all artifacts written so far. The adapter projects this onto the wire shape.</summary>
    public IReadOnlyCollection<ArtifactRef> Snapshot()
    {
        lock (_sync)
        {
            var copy = new ArtifactRef[_store.Count];
            var i = 0;
            foreach (var v in _store.Values)
            {
                copy[i++] = v.Ref;
            }

            return copy;
        }
    }

    /// <summary>Bytes for a given logical name, or null if no artifact was written under that name.</summary>
    public byte[]? GetBytes(string logicalName)
    {
        lock (_sync)
        {
            return _store.TryGetValue(logicalName, out var stored) ? stored.Bytes : null;
        }
    }

    private static string Sha256Hex(byte[] bytes)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private sealed record StoredArtifact(ArtifactRef Ref, byte[] Bytes);
}
