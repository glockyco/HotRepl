using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Protocol;
using Newtonsoft.Json;

namespace HotRepl.Sdk;

/// <summary>Client-side wrapper around an artifact reference.</summary>
public sealed class Artifact
{
    private static readonly HttpClient SharedHttp = new();

    private readonly object _gate = new();
    private readonly ArtifactRef _reference;
    private byte[]? _cachedBytes;

    internal Artifact(string logicalName, ArtifactRef reference)
    {
        LogicalName = logicalName;
        _reference = reference;
    }

    /// <summary>Logical artifact key from the command result map.</summary>
    public string LogicalName { get; }

    /// <summary>Raw protocol artifact reference.</summary>
    public ArtifactRef Reference => _reference;

    /// <summary>Read artifact bytes and verify SHA-256 when present.</summary>
    public async Task<byte[]> BytesAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedBytes is not null)
        {
            return _cachedBytes;
        }

        byte[] bytes;
        if (!string.IsNullOrEmpty(_reference.Path) && File.Exists(_reference.Path))
        {
            bytes = File.ReadAllBytes(_reference.Path);
        }
        else if (
            Uri.TryCreate(_reference.Uri, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, "file", StringComparison.Ordinal)
        )
        {
            bytes = File.ReadAllBytes(uri.LocalPath);
        }
        else if (Uri.TryCreate(_reference.Uri, UriKind.Absolute, out uri) && IsHttp(uri))
        {
            bytes = await SharedHttp.GetByteArrayAsync(uri).ConfigureAwait(false);
        }
        else
        {
            throw new HotReplProtocolException(
                "artifactUnreachable",
                $"Artifact '{LogicalName}' is not reachable via path or http(s)/file URI."
            );
        }

        cancellationToken.ThrowIfCancellationRequested();
        VerifyHash(bytes);
        lock (_gate)
        {
            _cachedBytes ??= bytes;
            return _cachedBytes;
        }
    }

    /// <summary>Read artifact text.</summary>
    public async Task<string> TextAsync(
        Encoding? encoding = null,
        CancellationToken cancellationToken = default
    )
    {
        var bytes = await BytesAsync(cancellationToken).ConfigureAwait(false);
        return (encoding ?? Encoding.UTF8).GetString(bytes);
    }

    /// <summary>Read artifact JSON.</summary>
    public async Task<T> JsonAsync<T>(CancellationToken cancellationToken = default)
    {
        var text = await TextAsync(Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return JsonConvert.DeserializeObject<T>(text)!;
    }

    private static bool IsHttp(Uri uri) =>
        string.Equals(uri.Scheme, "http", StringComparison.Ordinal)
        || string.Equals(uri.Scheme, "https", StringComparison.Ordinal);

    private void VerifyHash(byte[] bytes)
    {
        if (string.IsNullOrEmpty(_reference.Sha256))
        {
            return;
        }

        using var sha = SHA256.Create();
        var actual = ToHex(sha.ComputeHash(bytes));
        if (!string.Equals(actual, _reference.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new HotReplProtocolException(
                "artifactHashMismatch",
                $"SHA-256 mismatch for artifact '{LogicalName}'."
            );
        }
    }

    private static string ToHex(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
