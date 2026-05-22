using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control.Jobs;
using HotRepl.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ControlArtifactRef = HotRepl.Control.Artifacts.ArtifactRef;
using ProtocolArtifactRef = HotRepl.Protocol.ArtifactRef;

namespace HotRepl.Control;

/// <summary>Converts control-plane protocol messages into command handler calls and response messages.</summary>
internal sealed class ControlCommandRouter
{
    private readonly IControlCommandRegistry _registry;
    private readonly ControlJobManager? _jobs;
    private readonly ReplConfig _config;

    public ControlCommandRouter(
        IControlCommandRegistry registry,
        ControlJobManager? jobs = null,
        ReplConfig? config = null
    )
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _jobs = jobs;
        _config = config ?? new ReplConfig();
    }

    public CommandsListResultMessage List(string id) =>
        new()
        {
            Id = id,
            Commands = _registry.Describe().Select(ToSummary).ToArray(),
        };

    public object Describe(CommandDescribeMessage message)
    {
        if (!_registry.TryGet(message.Name, out var handler))
            return CommandError(
                message.Id,
                ErrorKind.UnknownCommand,
                "unknownCommand",
                $"Unknown control command '{message.Name}'.",
                retryable: false
            );

        return new CommandDescribeResultMessage
        {
            Id = message.Id,
            Descriptor = ToDescriptor(handler.Descriptor),
        };
    }

    public object Execute(CommandCallMessage message) => Execute(message, Guid.Empty);

    public object Execute(CommandCallMessage message, Guid connectionId)
    {
        if (!_registry.TryGet(message.Name, out var handler))
        {
            return CommandError(
                message.Id,
                ErrorKind.UnknownCommand,
                "unknownCommand",
                $"Unknown control command '{message.Name}'.",
                retryable: false
            );
        }

        return handler.Descriptor.Kind switch
        {
            ControlCommandKind.Job => StartJob(message, handler, connectionId),
            ControlCommandKind.Synchronous => ExecuteSynchronous(message, handler),
            _ => CommandError(
                message.Id,
                ErrorKind.UnsupportedOperation,
                "unsupportedCommandKind",
                $"Command '{message.Name}' has unsupported kind '{handler.Descriptor.Kind}'.",
                retryable: false
            ),
        };
    }

    private object StartJob(
        CommandCallMessage message,
        IControlCommandHandler handler,
        Guid connectionId
    )
    {
        if (_jobs == null)
            return CommandError(
                message.Id,
                ErrorKind.UnsupportedOperation,
                "jobsUnavailable",
                "Control jobs are not available.",
                retryable: false
            );

        try
        {
            var requestedTimeout = message.TimeoutMs.GetValueOrDefault();
            var timeout = requestedTimeout > 0
                ? TimeSpan.FromMilliseconds(requestedTimeout)
                : (TimeSpan?)null;
            var job = _jobs.StartJob(
                connectionId,
                message.Id,
                (context, token) =>
                    handler.ExecuteAsync(context.ToCommandContext(timeout), message.Args, token)
            );

            return new JobAcceptedMessage
            {
                Id = message.Id,
                JobId = job.JobId,
                State = ControlJobStates.Running,
            };
        }
        catch (InvalidOperationException ex)
            when (string.Equals(ex.Message, "maxJobConcurrency", StringComparison.Ordinal))
        {
            return CommandError(
                message.Id,
                ErrorKind.Busy,
                "jobConcurrencyLimit",
                "Maximum concurrent command jobs reached.",
                retryable: true
            );
        }
    }


    private CommandResultMessage ExecuteSynchronous(
        CommandCallMessage message,
        IControlCommandHandler handler
    )
    {
        try
        {
            var requestedTimeout = message.TimeoutMs.GetValueOrDefault();
            var timeout = requestedTimeout > 0
                ? TimeSpan.FromMilliseconds(requestedTimeout)
                : (TimeSpan?)null;
            var context = new ControlCommandContext(message.Id, timeout);
            var result = handler
                .ExecuteAsync(context, message.Args, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            if (IsResultTooLarge(result.Result))
            {
                return CommandError(
                    message.Id,
                    ErrorKind.Internal,
                    "resultTooLarge",
                    "Command output exceeds maxResultLength.",
                    retryable: false
                );
            }

            return new CommandResultMessage
            {
                Id = message.Id,
                Status = "ok",
                Output = result.Result,
                Artifacts = ToArtifactMap(result.Artifacts),
            };
        }
        catch (Exception ex)
        {
            return CommandError(
                message.Id,
                ErrorKind.Internal,
                "handlerException",
                ex.Message,
                retryable: false
            );
        }
    }

    public ValueTask RunJobAsync(string jobId)
    {
        if (_jobs == null)
            throw new InvalidOperationException("Control jobs are not available.");
        return _jobs.RunAsync(jobId);
    }

    public JobStatusResultMessage GetJobStatus(JobStatusMessage message) =>
        GetJobStatus(message, Guid.Empty) as JobStatusResultMessage
        ?? throw new InvalidOperationException("Expected job status result.");

    public object GetJobStatus(JobStatusMessage message, Guid connectionId)
    {
        if (!_jobs!.IsOwnedByConnection(message.JobId, connectionId))
            return JobOwnershipError(message.Id, message.JobId);

        var status = _jobs.GetStatus(message.JobId);
        if (!string.Equals(status.State, ControlJobStates.Running, StringComparison.Ordinal))
            return ToJobResult(message.Id, status);

        return new JobStatusResultMessage
        {
            Id = message.Id,
            JobId = status.JobId,
            State = status.State,
            Progress = status.Progress,
        };
    }


    public JobCancelResultMessage CancelJob(JobCancelMessage message) =>
        CancelJob(message, Guid.Empty) as JobCancelResultMessage
        ?? throw new InvalidOperationException("Expected job cancel result.");

    public object CancelJob(JobCancelMessage message, Guid connectionId)
    {
        if (!_jobs!.IsOwnedByConnection(message.JobId, connectionId))
            return JobOwnershipError(message.Id, message.JobId);

        var accepted = _jobs.Cancel(message.JobId);
        var status = _jobs.GetStatus(message.JobId);
        return new JobCancelResultMessage
        {
            Id = message.Id,
            Accepted = accepted,
            State = status.State,
        };
    }

    private static CommandSummary ToSummary(ControlCommandDescriptor descriptor) =>
        new()
        {
            Name = descriptor.Name,
            MajorVersion = descriptor.Version,
            Kind = descriptor.Kind == ControlCommandKind.Synchronous ? "sync" : "job",
            MutatesState = descriptor.MutatesState,
        };

    private static CommandDescriptor ToDescriptor(ControlCommandDescriptor descriptor) =>
        new()
        {
            Name = descriptor.Name,
            MajorVersion = descriptor.Version,
            Kind = descriptor.Kind == ControlCommandKind.Synchronous ? "sync" : "job",
            MutatesState = descriptor.MutatesState,
            InputSchema = descriptor.ArgsSchema,
            OutputSchema = descriptor.ResultSchema,
            ArtifactsSchema = descriptor.ArtifactsSchema,
        };

    private static ProtocolArtifactRef ToMessage(ControlArtifactRef artifact) =>
        new()
        {
            Uri = artifact.Uri,
            Path = artifact.Path,
            ContentType = artifact.ContentType,
            ByteSize = artifact.ByteSize,
            Sha256 = artifact.Sha256,
            Finalized = artifact.Finalized,
        };

    private static Dictionary<string, ProtocolArtifactRef> ToArtifactMap(
        IEnumerable<ControlArtifactRef> artifacts
    ) =>
        artifacts.ToDictionary(artifact => artifact.LogicalName, ToMessage, StringComparer.Ordinal);

    private static HotReplErrorEnvelope ToMessage(ControlCommandError error) =>
        new(error.Kind, error.Code, error.Message, error.Retryable, error.Details);

    private static CommandResultMessage JobOwnershipError(string id, string jobId) =>
        CommandError(
            id,
            ErrorKind.Conflict,
            "jobNotOwnedByConnection",
            $"Job '{jobId}' is not owned by this connection.",
            retryable: false
        );

    private static CommandResultMessage CommandError(
        string id,
        string kind,
        string code,
        string message,
        bool retryable
    ) =>
        new()
        {
            Id = id,
            Status = "failed",
            Error = new HotReplErrorEnvelope(kind, code, message, retryable, details: null),
        };

    private JobResultMessage ToJobResult(string id, ControlJobStatus status)
    {
        if (string.Equals(status.State, ControlJobStates.Completed, StringComparison.Ordinal))
        {
            if (status.Result != null && IsResultTooLarge(status.Result))
            {
                return new JobResultMessage
                {
                    Id = id,
                    JobId = status.JobId,
                    State = ControlJobStates.Failed,
                    Status = "failed",
                    Error = ResultTooLargeError(),
                    Artifacts = ToArtifactMap(status.Artifacts),
                };
            }

            return new JobResultMessage
            {
                Id = id,
                JobId = status.JobId,
                State = status.State,
                Status = "ok",
                Output = status.Result ?? new JObject(),
                Artifacts = ToArtifactMap(status.Artifacts),
            };
        }

        return new JobResultMessage
        {
            Id = id,
            JobId = status.JobId,
            State = status.State,
            Status = "failed",
            Error = ToTerminalJobError(status),
            Output = status.Result,
            Artifacts = ToArtifactMap(status.Artifacts),
        };
    }

    private bool IsResultTooLarge(JToken output) =>
        Encoding.UTF8.GetByteCount(output.ToString(Formatting.None)) > _config.MaxResultLength;

    private static HotReplErrorEnvelope ResultTooLargeError() =>
        new(
            ErrorKind.Internal,
            "resultTooLarge",
            "Command output exceeds maxResultLength.",
            retryable: false,
            details: null
        );

    private static HotReplErrorEnvelope ToTerminalJobError(ControlJobStatus status)
    {
        if (string.Equals(status.State, ControlJobStates.Cancelled, StringComparison.Ordinal))
        {
            return new HotReplErrorEnvelope(
                ErrorKind.Cancelled,
                "jobCancelled",
                $"Job '{status.JobId}' was cancelled.",
                retryable: false,
                details: null
            );
        }

        return status.Error == null
            ? new HotReplErrorEnvelope(
                ErrorKind.Internal,
                "missingJobError",
                "Job failed without an error.",
                retryable: false,
                details: null
            )
            : ToMessage(status.Error);
    }

}
