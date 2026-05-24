using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HotRepl.Control.Artifacts;

/// <summary>
/// In-memory <see cref="IArtifactWriter"/>. Suitable for the default
/// command-dispatch path: artifacts live for the duration of the
/// containing command invocation (or job lifetime), then are released
/// when the writer is no longer reachable.
/// </summary>
public sealed class InMemoryArtifactWriter : IArtifactWriter
{
    private const int BufferSize = 81920;

    private readonly object _sync = new();
    private readonly Dictionary<string, StoredArtifact> _store = new(StringComparer.Ordinal);
    private readonly string _uriPrefix;

    /// <summary>Create a writer that stamps memory artifact URIs with the given prefix.</summary>
    public InMemoryArtifactWriter(string uriPrefix = "hotrepl-artifact://memory/")
    {
        _uriPrefix = uriPrefix;
    }

    /// <inheritdoc />
    public ValueTask<ArtifactRef> AttachBytesAsync(
        string logicalName,
        ReadOnlyMemory<byte> data,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default
    )
    {
        ValidateLogicalName(logicalName);
        cancellationToken.ThrowIfCancellationRequested();

        var copy = data.ToArray();
        var artifact = CreateMemoryRef(logicalName, contentType, copy.LongLength, Sha256Hex(copy));
        Store(logicalName, artifact, copy);
        return new ValueTask<ArtifactRef>(artifact);
    }

    /// <inheritdoc />
    public async ValueTask<ArtifactRef> AttachStreamAsync(
        string logicalName,
        Stream stream,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default
    )
    {
        ValidateLogicalName(logicalName);
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var memory = new MemoryStream();
        var buffer = new byte[BufferSize];
        int read;
        while (
            (read = await ReadAsync(stream, buffer, cancellationToken).ConfigureAwait(false)) > 0
        )
        {
            hash.AppendData(buffer, 0, read);
            memory.Write(buffer, 0, read);
        }

        var bytes = memory.ToArray();
        var artifact = CreateMemoryRef(
            logicalName,
            contentType,
            bytes.LongLength,
            ToHex(hash.GetHashAndReset())
        );
        Store(logicalName, artifact, bytes);
        return artifact;
    }

    /// <inheritdoc />
    public async ValueTask<ArtifactRef> AttachFileAsync(
        string logicalName,
        string path,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default
    )
    {
        ValidateLogicalName(logicalName);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Artifact path required.", nameof(path));
        }

        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException($"Artifact source file not found: {path}", path);
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var stream = File.OpenRead(path))
        {
            var buffer = new byte[BufferSize];
            int read;
            while (
                (read = await ReadAsync(stream, buffer, cancellationToken).ConfigureAwait(false))
                > 0
            )
            {
                hash.AppendData(buffer, 0, read);
            }
        }

        return new ArtifactRef(
            LogicalName: logicalName,
            Uri: new Uri(Path.GetFullPath(path)).AbsoluteUri,
            Path: path,
            ContentType: contentType,
            ByteSize: info.Length,
            Sha256: ToHex(hash.GetHashAndReset()),
            Finalized: true
        );
    }

    /// <summary>Snapshot of all memory-backed artifacts written so far.</summary>
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

    /// <summary>Bytes for a given logical name, or null if no memory artifact was written under that name.</summary>
    public byte[]? GetBytes(string logicalName)
    {
        lock (_sync)
        {
            return _store.TryGetValue(logicalName, out var stored) ? stored.Bytes : null;
        }
    }

    private void Store(string logicalName, ArtifactRef artifact, byte[] bytes)
    {
        lock (_sync)
        {
            _store[logicalName] = new StoredArtifact(artifact, bytes);
        }
    }

    private ArtifactRef CreateMemoryRef(
        string logicalName,
        string contentType,
        long byteSize,
        string sha256
    ) =>
        new(
            LogicalName: logicalName,
            Uri: _uriPrefix + logicalName,
            Path: null,
            ContentType: contentType,
            ByteSize: byteSize,
            Sha256: sha256,
            Finalized: true
        );

    private static Task<int> ReadAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken
    )
    {
#if NETSTANDARD2_0
        return stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
#else
        return stream.ReadAsync(buffer.AsMemory(), cancellationToken).AsTask();
#endif
    }

    private static void ValidateLogicalName(string logicalName)
    {
        if (string.IsNullOrWhiteSpace(logicalName))
        {
            throw new ArgumentException("Logical name required.", nameof(logicalName));
        }
    }

    private static string Sha256Hex(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return ToHex(sha.ComputeHash(bytes));
    }

    private static string ToHex(byte[] hash)
    {
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private sealed record StoredArtifact(ArtifactRef Ref, byte[] Bytes);
}
