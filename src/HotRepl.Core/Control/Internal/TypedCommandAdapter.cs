using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control.Artifacts;
using HotRepl.Control.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control.Internal;

/// <summary>
/// Bridges a typed handler into the internal compiled-command shape the
/// router consumes. Validates args server-side, deserializes typed
/// args, runs the handler on the captured synchronization context, and
/// projects the typed result back to the wire shape.
/// </summary>
internal sealed class TypedCommandAdapter<TArgs, TOutput> : ICompiledControlCommand
{
    private readonly IControlCommandHandler<TArgs, TOutput> _inner;
    private readonly JsonSerializer _serializer;
    private readonly IControlCommandValidator _validator;

    public TypedCommandAdapter(
        IControlCommandHandler<TArgs, TOutput> inner,
        JsonSerializer serializer,
        IControlCommandValidator validator
    )
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        var attribute = (ControlCommandAttribute?)
            Attribute.GetCustomAttribute(
                inner.GetType(),
                typeof(ControlCommandAttribute),
                inherit: false
            );
        Descriptor = new ControlCommandDescriptor(
            name: attribute?.Name ?? inner.Name,
            version: attribute?.Version ?? inner.Version,
            kind: attribute?.Kind ?? inner.Kind,
            mutatesState: attribute?.MutatesState ?? inner.MutatesState,
            argsSchema: SchemaCache.For<TArgs>(),
            resultSchema: SchemaCache.For<TOutput>(),
            artifactsSchema: BuildArtifactsSchema(inner.GetType())
        );
    }

    /// <inheritdoc />
    public ControlCommandDescriptor Descriptor { get; }

    /// <inheritdoc />
    public async ValueTask<CompiledCommandResult> ExecuteAsync(
        CompiledCommandContext compiledContext,
        JObject args,
        CancellationToken cancellationToken
    )
    {
        // 1. Validate against the compiled args schema.
        var validation = _validator.Validate(args, SchemaCache.CompiledFor<TArgs>());
        if (!validation.Ok)
        {
            return new CompiledCommandResult(
                Succeeded: false,
                Output: new JObject(),
                Artifacts: Array.Empty<ArtifactRef>(),
                Diagnostics: new[] { validation.ToDiagnostic() }
            );
        }

        // 2. Deserialize typed args (EmptyArgs is special-cased).
        var typedArgs = DeserializeArgs(args);

        // 3. Build the typed context.
        var typedContext = BuildHandlerContext(compiledContext);

        // 4. Run the handler. ConfigureAwait(true) keeps continuations
        //    on the Unity sync context.
        var typedResult = await _inner
            .ExecuteAsync(typedContext, typedArgs, cancellationToken)
            .ConfigureAwait(true);

        // 5. Project the typed result to the wire shape.
        var outputJson = typedResult.Output is null
            ? new JObject()
            : JObject.FromObject(typedResult.Output, _serializer);

        var artifactList = typedResult.Artifacts.Values.ToArray();
        var diagnostics = typedResult.Diagnostics.Select(ToError).ToArray();

        return new CompiledCommandResult(
            Succeeded: typedResult.Succeeded,
            Output: outputJson,
            Artifacts: artifactList,
            Diagnostics: diagnostics
        );
    }

    private TArgs DeserializeArgs(JObject args)
    {
        if (typeof(TArgs) == typeof(EmptyArgs))
        {
            return default!;
        }

        return args.ToObject<TArgs>(_serializer)
            ?? throw new InvalidOperationException(
                $"Newtonsoft deserialized {typeof(TArgs).Name} as null."
            );
    }

    private static ControlCommandContext<TOutput> BuildHandlerContext(
        CompiledCommandContext compiled
    )
    {
        IProgress<ControlCommandProgress> progress = compiled.ProgressSink is null
            ? SilentProgress.Instance
            : new ProgressSinkAdapter(compiled.ProgressSink);
        return new ControlCommandContext<TOutput>(
            requestId: compiled.RequestId,
            timeout: compiled.Timeout,
            jobId: compiled.JobId,
            progress: progress,
            artifacts: compiled.Artifacts
        );
    }

    private static ControlCommandError ToError(ControlCommandDiagnostic diagnostic) =>
        new(
            Kind: DiagnosticKindToWire(diagnostic.Kind),
            Code: diagnostic.Code,
            Message: diagnostic.Message,
            Retryable: diagnostic.Retryable,
            Details: diagnostic.Details is null ? null : JObject.FromObject(diagnostic.Details)
        );

    private static JObject BuildArtifactsSchema(Type handlerType)
    {
        var attributes = handlerType
            .GetCustomAttributes(typeof(ControlCommandArtifactAttribute), inherit: false)
            .Cast<ControlCommandArtifactAttribute>()
            .ToArray();

        if (attributes.Length == 0)
        {
            return SchemaCache.AnyObject;
        }

        var schema = new JObject
        {
            ["type"] = "object",
            ["patternProperties"] = new JObject(),
            ["required"] = new JArray(),
            ["additionalProperties"] = false,
        };

        var patternProperties = (JObject)schema["patternProperties"]!;
        var required = (JArray)schema["required"]!;

        foreach (var attribute in attributes)
        {
            patternProperties[ConvertKeyPatternToRegex(attribute.KeyPattern)] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["uri"] = new JObject { ["type"] = "string" },
                    ["path"] = new JObject { ["type"] = new JArray("string", "null") },
                    ["sha256"] = new JObject { ["type"] = "string" },
                    ["byteSize"] = new JObject { ["type"] = "integer" },
                    ["contentType"] = new JObject
                    {
                        ["type"] = "string",
                        ["const"] = attribute.ContentType,
                    },
                    ["finalized"] = new JObject { ["type"] = "boolean" },
                },
                ["required"] = new JArray("uri", "sha256", "byteSize", "contentType", "finalized"),
                ["additionalProperties"] = true,
            };

            if (attribute.Required && !attribute.KeyPattern.Contains('<'))
            {
                required.Add(attribute.KeyPattern);
            }
        }

        return schema;
    }

    private static string ConvertKeyPatternToRegex(string keyPattern)
    {
        return "^"
            + Regex.Escape(keyPattern).Replace("<stem>", "[^.]+", StringComparison.Ordinal)
            + "$";
    }

    private static string DiagnosticKindToWire(ControlCommandDiagnosticKind kind) =>
        kind switch
        {
            ControlCommandDiagnosticKind.Info => "info",
            ControlCommandDiagnosticKind.Warning => "warning",
            ControlCommandDiagnosticKind.ValidationFailed => "validation_failed",
            ControlCommandDiagnosticKind.PreconditionFailed => "precondition_failed",
            ControlCommandDiagnosticKind.Conflict => "conflict",
            ControlCommandDiagnosticKind.Cancelled => "cancelled",
            _ => "internal",
        };
}
