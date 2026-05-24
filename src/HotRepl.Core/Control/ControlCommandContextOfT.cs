using System;
using System.Collections.Generic;
using HotRepl.Control.Artifacts;

namespace HotRepl.Control;

/// <summary>
/// Generic-binding control-command context. Exposes result helpers as
/// instance methods so the compiler infers <typeparamref name="TOutput"/>
/// from the handler signature.
/// </summary>
/// <typeparam name="TOutput">The command output payload type.</typeparam>
public sealed class ControlCommandContext<TOutput> : ControlCommandContext
{
    /// <summary>Construct a generic command context.</summary>
    public ControlCommandContext(
        string requestId,
        TimeSpan? timeout,
        string? jobId,
        IProgress<ControlCommandProgress>? progress,
        IArtifactWriter artifacts
    )
        : base(
            requestId,
            timeout,
            jobId,
            progress ?? new Progress<ControlCommandProgress>(),
            artifacts
        ) { }

    /// <summary>Success with no artifacts and no diagnostics.</summary>
    public ControlCommandResult<TOutput> Ok(TOutput output) => new() { Output = output };

    /// <summary>Success with a single artifact attached at the top level.</summary>
    public ControlCommandResult<TOutput> Ok(
        TOutput output,
        string artifactName,
        ArtifactRef artifact
    ) => ControlCommandResult.Ok(output, artifactName, artifact);

    /// <summary>Success with a pre-built artifact dictionary.</summary>
    public ControlCommandResult<TOutput> Ok(
        TOutput output,
        IReadOnlyDictionary<string, ArtifactRef> artifacts
    ) => new() { Output = output, Artifacts = artifacts };

    /// <summary>Failure: a runtime precondition was not satisfied.</summary>
    public ControlCommandResult<TOutput> PreconditionFailed(
        string code,
        string message,
        object? details = null
    ) =>
        Failed(
            new ControlCommandDiagnostic(
                ControlCommandDiagnosticKind.PreconditionFailed,
                code,
                message,
                Retryable: false,
                Details: details
            )
        );

    /// <summary>Failure: argument schema or business-rule violation.</summary>
    public ControlCommandResult<TOutput> ValidationFailed(
        string code,
        string message,
        object? details = null
    ) =>
        Failed(
            new ControlCommandDiagnostic(
                ControlCommandDiagnosticKind.ValidationFailed,
                code,
                message,
                Retryable: false,
                Details: details
            )
        );

    /// <summary>Failure constructed from an explicit diagnostic.</summary>
    public ControlCommandResult<TOutput> Failed(ControlCommandDiagnostic diagnostic) =>
        new() { Succeeded = false, Diagnostics = new[] { diagnostic } };
}
