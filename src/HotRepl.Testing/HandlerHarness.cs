using System;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.Control.Artifacts;
using HotRepl.Control.Schema;
using Newtonsoft.Json.Linq;

namespace HotRepl.Testing;

/// <summary>In-process helpers for testing typed command handlers.</summary>
public static class HandlerHarness
{
    /// <summary>Generate the JSON schema HotRepl exposes for a type.</summary>
    public static JObject GenerateSchema<T>() => SchemaCache.For<T>();

    /// <summary>Validate a JSON object string against the schema for <typeparamref name="TArgs"/>.</summary>
    public static SchemaValidationResult Validate<TArgs>(string json) =>
        Validate<TArgs>(JObject.Parse(json));

    /// <summary>Validate a JSON object against the schema for <typeparamref name="TArgs"/>.</summary>
    public static SchemaValidationResult Validate<TArgs>(JObject args) =>
        new NJsonSchemaValidator().Validate(args, SchemaCache.CompiledFor<TArgs>());

    /// <summary>Run a handler in process with a test command context.</summary>
    public static async Task<HandlerResult<TOutput>> RunAsync<TArgs, TOutput>(
        IControlCommandHandler<TArgs, TOutput> handler,
        TArgs args,
        IArtifactWriter? artifactWriter = null,
        CancellationToken cancellationToken = default
    )
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var context = new ControlCommandContext<TOutput>(
            requestId: "test-1",
            timeout: TimeSpan.FromSeconds(30),
            jobId: null,
            progress: null,
            artifacts: artifactWriter ?? new InMemoryArtifactWriter()
        );
        var result = await handler
            .ExecuteAsync(context, args, cancellationToken)
            .ConfigureAwait(false);
        return new HandlerResult<TOutput>(
            result.Succeeded,
            result.Output,
            result.Artifacts,
            result.Diagnostics
        );
    }
}
