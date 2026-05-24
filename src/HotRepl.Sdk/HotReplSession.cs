using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Protocol;
using HotRepl.Sdk.Internal;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Sdk;

/// <summary>Connected HotRepl protocol session.</summary>
public sealed class HotReplSession : IAsyncDisposable
{
    private readonly MessageDispatcher _dispatcher;
    private readonly HotReplClientOptions _options;
    private readonly JsonSerializer _serializer;
    private readonly ConcurrentDictionary<string, CommandDescriptor> _descriptors = new(
        StringComparer.Ordinal
    );
    private IReadOnlyList<CommandSummary>? _cachedCatalog;
    private int _idCounter;

    internal HotReplSession(
        MessageDispatcher dispatcher,
        HotReplCapabilities capabilities,
        HotReplClientOptions options
    )
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _serializer = JsonSerializer.CreateDefault(_options.SerializerSettings);
    }

    /// <summary>Capabilities received from the runtime handshake.</summary>
    public HotReplCapabilities Capabilities { get; }

    internal string NextId(string prefix) => $"{prefix}-{Interlocked.Increment(ref _idCounter)}";

    internal async Task<JObject> RequestRawAsync(
        JObject message,
        CancellationToken cancellationToken
    )
    {
        var id = message["id"]?.ToString();
        if (string.IsNullOrEmpty(id))
        {
            throw new HotReplProtocolException(
                "missingRequestId",
                "Request payload is missing id."
            );
        }

        var pending = _dispatcher.ExpectResponseAsync(
            id!,
            _options.RequestTimeout,
            cancellationToken
        );
        await _dispatcher.SendAsync(message, cancellationToken).ConfigureAwait(false);
        return await pending.ConfigureAwait(false);
    }

    /// <summary>List available commands, cached for the lifetime of this session.</summary>
    public async Task<IReadOnlyList<CommandSummary>> ListCommandsAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (_cachedCatalog is not null)
        {
            return _cachedCatalog;
        }

        var id = NextId("list");
        var response = await RequestRawAsync(
                new JObject { ["type"] = "commands_list", ["id"] = id },
                cancellationToken
            )
            .ConfigureAwait(false);
        var commands = response["commands"] as JArray ?? new JArray();
        var list = commands.OfType<JObject>().Select(ParseCommandSummary).ToArray();
        _cachedCatalog = list;
        return list;
    }

    /// <summary>Describe one command, cached per session after the first request.</summary>
    public async Task<CommandDescriptor> DescribeCommandAsync(
        string name,
        CancellationToken cancellationToken = default
    )
    {
        if (_descriptors.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var id = NextId("describe");
        var response = await RequestRawAsync(
                new JObject
                {
                    ["type"] = "command_describe",
                    ["id"] = id,
                    ["name"] = name,
                },
                cancellationToken
            )
            .ConfigureAwait(false);
        var descriptor = response["descriptor"]!.ToObject<CommandDescriptor>(_serializer)!;
        _descriptors[name] = descriptor;
        return descriptor;
    }

    /// <summary>Run a synchronous typed command.</summary>
    public Task<HotReplResult<TResult>> RunAsync<TArgs, TResult>(
        string command,
        TArgs args,
        HotReplRunOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var argsJson = args is null ? new JObject() : JObject.FromObject(args, _serializer);
        return RunInternalAsync<TResult>(command, argsJson, options, cancellationToken);
    }

    /// <summary>Run a synchronous command with raw JSON args.</summary>
    public Task<HotReplResult<JToken>> RunRawAsync(
        string command,
        JToken args,
        HotReplRunOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var argsJson = args as JObject ?? new JObject { ["value"] = args };
        return RunInternalAsync<JToken>(command, argsJson, options, cancellationToken);
    }

    internal HotReplResult<TResult> ToTypedResult<TResult>(JObject response)
    {
        if (string.Equals((string?)response["status"], "failed", StringComparison.Ordinal))
        {
            var error = (JObject?)response["error"];
            throw new HotReplCommandException(
                ParseErrorKind((string?)error?["kind"]),
                (string?)error?["code"] ?? "commandFailed",
                (string?)error?["message"] ?? "Command failed.",
                (bool?)error?["retryable"] ?? false,
                error?["details"]
            );
        }

        var outputJson = response["output"] ?? new JObject();
        var output = outputJson.ToObject<TResult>(_serializer)!;
        return new HotReplResult<TResult>(output, ParseArtifacts(response["artifacts"] as JObject));
    }

    internal HotReplResult<TResult> ParseJobTerminal<TResult>(JObject response, string jobId)
    {
        if (string.Equals((string?)response["status"], "failed", StringComparison.Ordinal))
        {
            var error = (JObject?)response["error"];
            throw new HotReplJobFailedException(
                jobId,
                (string?)error?["code"] ?? "jobFailed",
                (string?)error?["message"] ?? "Job failed.",
                error?["details"]
            );
        }

        var outputJson = response["output"] ?? new JObject();
        var output = outputJson.ToObject<TResult>(_serializer)!;
        return new HotReplResult<TResult>(output, ParseArtifacts(response["artifacts"] as JObject));
    }

    /// <summary>Close the session.</summary>
    public ValueTask DisposeAsync() => _dispatcher.DisposeAsync();

    private async Task<HotReplResult<TResult>> RunInternalAsync<TResult>(
        string command,
        JObject args,
        HotReplRunOptions? options,
        CancellationToken cancellationToken
    )
    {
        var catalog = await ListCommandsAsync(cancellationToken).ConfigureAwait(false);
        var entry = catalog.FirstOrDefault(c =>
            string.Equals(c.Name, command, StringComparison.Ordinal)
        );
        if (entry is null)
        {
            throw new HotReplCommandException(
                HotReplErrorKind.InvalidRequest,
                "commandNotFound",
                $"Command '{command}' is not registered.",
                retryable: false
            );
        }

        var id = NextId("run");
        var message = new JObject
        {
            ["type"] = "command_call",
            ["id"] = id,
            ["name"] = command,
            ["args"] = args,
        };
        if (options?.Timeout is TimeSpan timeout)
        {
            message["timeoutMs"] = (long)timeout.TotalMilliseconds;
        }

        var response = await RequestRawAsync(message, cancellationToken).ConfigureAwait(false);
        var responseType = (string?)response["type"];
        if (string.Equals(entry.Kind, "sync", StringComparison.Ordinal))
        {
            if (!string.Equals(responseType, "command_result", StringComparison.Ordinal))
            {
                throw new HotReplProtocolException(
                    "expectedCommandResult",
                    $"Expected command_result, got '{responseType}'."
                );
            }

            return ToTypedResult<TResult>(response);
        }

        throw new HotReplProtocolException(
            "syncDispatchOnJob",
            $"Command '{command}' is a job; use StartJobAsync."
        );
    }

    private Dictionary<string, Artifact> ParseArtifacts(JObject? json)
    {
        var artifacts = new Dictionary<string, Artifact>(StringComparer.Ordinal);
        if (json is null)
        {
            return artifacts;
        }

        foreach (var property in json.Properties())
        {
            artifacts[property.Name] = new Artifact(
                property.Name,
                property.Value.ToObject<ArtifactRef>(_serializer)!
            );
        }

        return artifacts;
    }

    private static CommandSummary ParseCommandSummary(JObject value) =>
        value.ToObject<CommandSummary>()!;

    private static HotReplErrorKind ParseErrorKind(string? kind) =>
        kind switch
        {
            "invalid_request" => HotReplErrorKind.InvalidRequest,
            "validation_failed" => HotReplErrorKind.ValidationFailed,
            "precondition_failed" => HotReplErrorKind.PreconditionFailed,
            "conflict" => HotReplErrorKind.Conflict,
            "cancelled" => HotReplErrorKind.Cancelled,
            "unsupported_operation" => HotReplErrorKind.UnsupportedOperation,
            _ => HotReplErrorKind.Internal,
        };
}
