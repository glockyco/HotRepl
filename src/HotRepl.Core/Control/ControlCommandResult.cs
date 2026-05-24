using System;
using System.Collections.Generic;
using HotRepl.Control.Artifacts;

namespace HotRepl.Control;

/// <summary>
/// Result returned by a typed control-command handler. Carries the
/// typed <typeparamref name="TOutput"/>, top-level artifact references,
/// and a diagnostic list. The adapter projects this onto the wire
/// shape consumed by clients.
/// </summary>
public sealed class ControlCommandResult<TOutput>
{
    private static readonly IReadOnlyDictionary<string, ArtifactRef> EmptyArtifacts =
        new Dictionary<string, ArtifactRef>(0, StringComparer.Ordinal);

    private static readonly IReadOnlyList<ControlCommandDiagnostic> EmptyDiagnostics =
        Array.Empty<ControlCommandDiagnostic>();

    /// <summary>The typed result payload. Null on failure.</summary>
    public TOutput? Output { get; init; }

    /// <summary>
    /// Top-level artifacts produced by the handler, keyed by logical name.
    /// Artifacts are projected onto the wire <c>artifacts</c> envelope at the
    /// same level as <see cref="Output"/>; do not nest <see cref="ArtifactRef"/>
    /// inside <typeparamref name="TOutput"/>.
    /// </summary>
    public IReadOnlyDictionary<string, ArtifactRef> Artifacts { get; init; } = EmptyArtifacts;

    /// <summary>
    /// Informational, warning, or failure diagnostics. Presence of a failure
    /// diagnostic together with <see cref="Succeeded"/> = false drives the
    /// wire <c>status = "failed"</c> shape.
    /// </summary>
    public IReadOnlyList<ControlCommandDiagnostic> Diagnostics { get; init; } = EmptyDiagnostics;

    /// <summary>True for handler-reported success.</summary>
    public bool Succeeded { get; init; } = true;

    // ---- factories ----

    /// <summary>Success with no artifacts and no diagnostics.</summary>
    public static ControlCommandResult<TOutput> Ok(TOutput output) => new() { Output = output };

    /// <summary>Success with a single artifact attached at the top level.</summary>
    public static ControlCommandResult<TOutput> Ok(
        TOutput output,
        string artifactName,
        ArtifactRef artifact
    ) =>
        new()
        {
            Output = output,
            Artifacts = new Dictionary<string, ArtifactRef>(StringComparer.Ordinal)
            {
                [artifactName] = artifact,
            },
        };

    /// <summary>Success with a pre-built artifact dictionary.</summary>
    public static ControlCommandResult<TOutput> Ok(
        TOutput output,
        IReadOnlyDictionary<string, ArtifactRef> artifacts
    ) => new() { Output = output, Artifacts = artifacts };

    /// <summary>Failure: argument schema or business-rule violation; handler did not act.</summary>
    public static ControlCommandResult<TOutput> ValidationFailed(
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

    /// <summary>Failure: a runtime precondition was not satisfied.</summary>
    public static ControlCommandResult<TOutput> PreconditionFailed(
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

    /// <summary>Failure constructed from an explicit diagnostic.</summary>
    public static ControlCommandResult<TOutput> Failed(ControlCommandDiagnostic diagnostic) =>
        new() { Succeeded = false, Diagnostics = new[] { diagnostic } };
}
