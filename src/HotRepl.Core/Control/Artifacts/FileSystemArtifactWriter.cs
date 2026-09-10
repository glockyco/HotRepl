using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HotRepl.Control.Artifacts;

/// <summary>
/// File-backed <see cref="IArtifactWriter"/>. Writes each attachment into its own
/// directory and returns a reference that carries both <c>path</c> and a
/// <c>file://</c> URI, so a client on the same machine can read the bytes after
/// the producing command or job has finished.
/// </summary>
/// <remarks>
/// The directory is created on the first attachment, so a command that attaches
/// nothing leaves no directory behind. Files outlive the process; the client owns
/// their removal.
/// </remarks>
public sealed class FileSystemArtifactWriter : IArtifactWriter
{
    private const int BufferSize = 81920;

    private readonly object _sync = new();
    private readonly string _directory;

    /// <summary>Create a writer that stores artifacts in <paramref name="directory"/>.</summary>
    public FileSystemArtifactWriter(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Artifact directory required.", nameof(directory));
        }

        _directory = Path.GetFullPath(directory);
    }

    /// <summary>Directory that holds every artifact this writer produces.</summary>
    public string Directory => _directory;

    /// <summary>
    /// Directory for one command invocation or job under <paramref name="root"/>.
    /// </summary>
    /// <remarks>
    /// A request id arrives from the client, so the scope is reduced to a single safe path segment
    /// and cannot select a directory outside <paramref name="root"/>.
    /// </remarks>
    public static string ScopeDirectory(string root, string scope)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("Artifact directory required.", nameof(root));
        }

        return Path.Combine(root, SafeSegment(scope, "command"));
    }

    /// <inheritdoc />
    public async ValueTask<ArtifactRef> AttachBytesAsync(
        string logicalName,
        ReadOnlyMemory<byte> data,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default
    )
    {
        var path = CreateArtifactPath(logicalName);
        cancellationToken.ThrowIfCancellationRequested();

        var bytes = data.ToArray();
        using (var file = File.Create(path))
        {
            await WriteAsync(file, bytes, bytes.Length, cancellationToken).ConfigureAwait(false);
        }

        return CreateRef(logicalName, path, contentType, bytes.LongLength, Sha256Hex(bytes));
    }

    /// <inheritdoc />
    public async ValueTask<ArtifactRef> AttachStreamAsync(
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

        var path = CreateArtifactPath(logicalName);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long size = 0;
        using (var file = File.Create(path))
        {
            var buffer = new byte[BufferSize];
            int read;
            while (
                (read = await ReadAsync(stream, buffer, cancellationToken).ConfigureAwait(false))
                > 0
            )
            {
                hash.AppendData(buffer, 0, read);
                await WriteAsync(file, buffer, read, cancellationToken).ConfigureAwait(false);
                size += read;
            }
        }

        return CreateRef(logicalName, path, contentType, size, ToHex(hash.GetHashAndReset()));
    }

    /// <inheritdoc />
    /// <remarks>
    /// References the file where it already lives instead of copying it, so a
    /// handler that writes its own output keeps ownership of that file.
    /// </remarks>
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

        return CreateRef(
            logicalName,
            Path.GetFullPath(path),
            contentType,
            info.Length,
            ToHex(hash.GetHashAndReset())
        );
    }

    private string CreateArtifactPath(string logicalName)
    {
        ValidateLogicalName(logicalName);
        lock (_sync)
        {
            System.IO.Directory.CreateDirectory(_directory);
        }

        return Path.Combine(_directory, SafeSegment(logicalName, "artifact"));
    }

    private static ArtifactRef CreateRef(
        string logicalName,
        string path,
        string contentType,
        long byteSize,
        string sha256
    ) =>
        new(
            LogicalName: logicalName,
            Uri: new Uri(path).AbsoluteUri,
            Path: path,
            ContentType: contentType,
            ByteSize: byteSize,
            Sha256: sha256,
            Finalized: true
        );

    /// <summary>
    /// Reduces a name to one path segment. A logical name comes from a command handler and a scope
    /// comes from the client, so anything outside the allowed set becomes an underscore and no
    /// name can traverse out of its directory.
    /// </summary>
    private static string SafeSegment(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            var keep =
                (c >= 'a' && c <= 'z')
                || (c >= 'A' && c <= 'Z')
                || (c >= '0' && c <= '9')
                || c == '.'
                || c == '-'
                || c == '_';
            sb.Append(keep ? c : '_');
        }

        var segment = sb.ToString().TrimStart('.');
        return segment.Length == 0 ? fallback : segment;
    }

    private static Task WriteAsync(
        Stream stream,
        byte[] buffer,
        int count,
        CancellationToken cancellationToken
    )
    {
#if NETSTANDARD2_0
        return stream.WriteAsync(buffer, 0, count, cancellationToken);
#else
        return stream.WriteAsync(buffer.AsMemory(0, count), cancellationToken).AsTask();
#endif
    }

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
}
