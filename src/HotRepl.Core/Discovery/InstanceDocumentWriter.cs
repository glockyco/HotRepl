using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace HotRepl.Discovery;

internal sealed class InstanceDocumentWriter : IDisposable
{
    private const int FingerprintHexChars = 8;
    private readonly string _path;
    private bool _disposed;

    private InstanceDocumentWriter(string path) => _path = path;

    public string Path => _path;

    public static InstanceDocumentWriter Write(
        ReplConfig config,
        HostInfo host,
        string? root = null,
        string? instanceId = null
    )
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));
        if (host == null)
            throw new ArgumentNullException(nameof(host));

        var directory = root ?? GetDefaultRoot();
        Directory.CreateDirectory(directory);

        var resolvedInstanceId = instanceId ?? CreateInstanceId(host, config);
        var path = System.IO.Path.Combine(directory, resolvedInstanceId + ".json");
        var tempPath = path + ".tmp";
        var process = Process.GetCurrentProcess();
        var document = new InstanceDocument
        {
            SchemaVersion = 1,
            InstanceId = resolvedInstanceId,
            Url = $"ws://{config.BindHost}:{config.Port}",
            BindHost = config.BindHost,
            Port = config.Port,
            StartedAt = DateTimeOffset.UtcNow,
            Process = new ProcessDocument { Id = process.Id, Name = process.ProcessName },
            Host = new HostDocument
            {
                Name = host.Name,
                Runtime = host.Runtime,
                Platform = host.Platform,
            },
            ControlPlane = new ControlPlaneDocument
            {
                Supported = config.ControlPlaneEnabled,
                ProtocolVersion = 1,
                AuthRequired =
                    config.RequireControlAuth || !string.IsNullOrEmpty(config.ControlAuthToken),
                LeaseRequired = config.RequireControlLease,
            },
            Auth = new AuthDocument
            {
                Required =
                    config.RequireControlAuth || !string.IsNullOrEmpty(config.ControlAuthToken),
                Fingerprint = Fingerprint(config.ControlAuthToken),
            },
        };

        var json = JsonConvert.SerializeObject(document, Formatting.Indented);
        File.WriteAllText(tempPath, json, Encoding.UTF8);
        if (File.Exists(path))
            File.Delete(path);
        File.Move(tempPath, path);
        return new InstanceDocumentWriter(path);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string GetDefaultRoot()
    {
        var xdgRuntime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(xdgRuntime))
            return System.IO.Path.Combine(xdgRuntime, "hotrepl", "instances");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData
            );
            return System.IO.Path.Combine(localAppData, "HotRepl", "instances");
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
            return System.IO.Path.Combine(appData, "HotRepl", "instances");

        return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hotrepl", "instances");
    }

    private static string CreateInstanceId(HostInfo host, ReplConfig config) =>
        string.Join(
            "-",
            SanitizeIdPart(host.Name),
            config.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.UtcNow.ToString(
                "yyyyMMddTHHmmssZ",
                System.Globalization.CultureInfo.InvariantCulture
            )
        );

    private static string SanitizeIdPart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "hotrepl";

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        return builder.ToString();
    }

    private static string? Fingerprint(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return null;

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(token));
        var builder = new StringBuilder("sha256:".Length + FingerprintHexChars);
        builder.Append("sha256:");
        for (
            var i = 0;
            i < hash.Length && builder.Length < "sha256:".Length + FingerprintHexChars;
            i++
        )
            builder.Append(
                hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture)
            );
        return builder.ToString();
    }

    private sealed class InstanceDocument
    {
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonProperty("instanceId")]
        public string InstanceId { get; set; } = string.Empty;

        [JsonProperty("url")]
        public string Url { get; set; } = string.Empty;

        [JsonProperty("bindHost")]
        public string BindHost { get; set; } = string.Empty;

        [JsonProperty("port")]
        public int Port { get; set; }

        [JsonProperty("startedAt")]
        public DateTimeOffset StartedAt { get; set; }

        [JsonProperty("process")]
        public ProcessDocument Process { get; set; } = new();

        [JsonProperty("host")]
        public HostDocument Host { get; set; } = new();

        [JsonProperty("controlPlane")]
        public ControlPlaneDocument ControlPlane { get; set; } = new();

        [JsonProperty("auth")]
        public AuthDocument Auth { get; set; } = new();
    }

    private sealed class ProcessDocument
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class HostDocument
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("runtime")]
        public string Runtime { get; set; } = string.Empty;

        [JsonProperty("platform")]
        public string Platform { get; set; } = string.Empty;
    }

    private sealed class ControlPlaneDocument
    {
        [JsonProperty("supported")]
        public bool Supported { get; set; }

        [JsonProperty("protocolVersion")]
        public int ProtocolVersion { get; set; }

        [JsonProperty("authRequired")]
        public bool AuthRequired { get; set; }

        [JsonProperty("leaseRequired")]
        public bool LeaseRequired { get; set; }
    }

    private sealed class AuthDocument
    {
        [JsonProperty("required")]
        public bool Required { get; set; }

        [JsonProperty("fingerprint")]
        public string? Fingerprint { get; set; }
    }
}
