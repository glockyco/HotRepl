using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control.Artifacts;
using HotRepl.Control.Jobs;
using HotRepl.Protocol;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control;

/// <summary>Converts control-plane protocol messages into command handler calls and response messages.</summary>
internal sealed class ControlCommandRouter
{
    private readonly IControlCommandRegistry _registry;
    private readonly ControlSessionManager? _sessions;
    private readonly ControlJobManager? _jobs;

    public ControlCommandRouter(
        IControlCommandRegistry registry,
        ControlSessionManager? sessions = null,
        ControlJobManager? jobs = null
    )
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _sessions = sessions;
        _jobs = jobs;
    }

    public CommandDescribeResultMessage Describe(string id)
    {
        return new CommandDescribeResultMessage
        {
            Id = id,
            Commands = _registry.Describe().Select(ToMessage).ToArray(),
        };
    }

    public object Execute(CommandCallMessage message) => Execute(message, Guid.Empty);

    public object Execute(CommandCallMessage message, Guid connectionId)
    {
        if (!_registry.TryGet(message.Name, out var handler))
        {
            return CommandError(
                message.Id,
                "unknown_command",
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
                "unsupported_operation",
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
                "unsupported_operation",
                "jobsUnavailable",
                "Control jobs are not available.",
                retryable: false
            );

        var timeout =
            message.TimeoutMs > 0 ? TimeSpan.FromMilliseconds(message.TimeoutMs) : (TimeSpan?)null;
        var job = _jobs.StartJob(
            connectionId,
            message.Id,
            leaseId: null,
            idempotencyKey: null,
            (context, token) =>
                handler.ExecuteAsync(context.ToCommandContext(timeout), message.Args, token)
        );

        return new CommandAcceptedMessage
        {
            Id = message.Id,
            JobId = job.JobId,
            State = ControlJobStates.Running,
        };
    }

    private static object ExecuteSynchronous(
        CommandCallMessage message,
        IControlCommandHandler handler
    )
    {
        try
        {
            var timeout =
                message.TimeoutMs > 0
                    ? TimeSpan.FromMilliseconds(message.TimeoutMs)
                    : (TimeSpan?)null;
            var context = new ControlCommandContext(message.Id, null, null, timeout);
            var result = handler
                .ExecuteAsync(context, message.Args, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
            return new CommandResultMessage
            {
                Id = message.Id,
                Status = "ok",
                Result = result.Result,
                Artifacts = result.Artifacts.Select(ToMessage).ToArray(),
                Diagnostics = result.Diagnostics.Select(ToMessage).ToArray(),
            };
        }
        catch (Exception ex)
        {
            return CommandError(
                message.Id,
                "internal",
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
        return new JobStatusResultMessage
        {
            Id = message.Id,
            JobId = status.JobId,
            State = status.State,
            Progress = status.Progress,
        };
    }

    public object GetJobResult(JobResultRequestMessage message) =>
        GetJobResult(message, Guid.Empty);

    public object GetJobResult(JobResultRequestMessage message, Guid connectionId)
    {
        if (!_jobs!.IsOwnedByConnection(message.JobId, connectionId))
            return JobOwnershipError(message.Id, message.JobId);

        var status = _jobs.GetStatus(message.JobId);
        if (string.Equals(status.State, ControlJobStates.Running, StringComparison.Ordinal))
        {
            return CommandError(
                message.Id,
                "busy",
                "jobNotTerminal",
                $"Job '{message.JobId}' is still {status.State}.",
                retryable: true
            );
        }

        if (string.Equals(status.State, ControlJobStates.Failed, StringComparison.Ordinal))
        {
            return new CommandErrorMessage
            {
                Id = message.Id,
                Status = "failed",
                Error = ToMessage(
                    status.Error
                        ?? new ControlCommandError(
                            "internal",
                            "missingJobError",
                            "Job failed without an error.",
                            Retryable: false,
                            Details: new JObject()
                        )
                ),
                Diagnostics = status.Diagnostics.Select(ToMessage).ToArray(),
            };
        }

        if (string.Equals(status.State, ControlJobStates.Cancelled, StringComparison.Ordinal))
            return CommandError(
                message.Id,
                "cancelled",
                "jobCancelled",
                $"Job '{message.JobId}' was cancelled.",
                retryable: false
            );

        return new JobResultMessage
        {
            Id = message.Id,
            JobId = status.JobId,
            State = status.State,
            Status = "ok",
            Result = status.Result ?? new JObject(),
            Artifacts = status.Artifacts.Select(ToMessage).ToArray(),
            Diagnostics = status.Diagnostics.Select(ToMessage).ToArray(),
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

    private static CommandDescriptorMessage ToMessage(ControlCommandDescriptor descriptor) =>
        new()
        {
            Name = descriptor.Name,
            Version = descriptor.Version,
            Kind = descriptor.Kind == ControlCommandKind.Synchronous ? "sync" : "job",
            MutatesState = descriptor.MutatesState,
            ArgsSchema = descriptor.ArgsSchema,
            ResultSchema = descriptor.ResultSchema,
        };

    private static ArtifactRefMessage ToMessage(ArtifactRef artifact) =>
        new()
        {
            LogicalName = artifact.LogicalName,
            Uri = artifact.Uri,
            Path = artifact.Path,
            ContentType = artifact.ContentType,
            ByteSize = artifact.ByteSize,
            Sha256 = artifact.Sha256,
            Finalized = artifact.Finalized,
        };

    private static ControlErrorMessage ToMessage(ControlCommandError error) =>
        new()
        {
            Kind = error.Kind,
            Code = error.Code,
            Message = error.Message,
            Retryable = error.Retryable,
            Details = error.Details,
        };

    private static CommandErrorMessage JobOwnershipError(string id, string jobId) =>
        CommandError(
            id,
            "conflict",
            "jobNotOwnedByConnection",
            $"Job '{jobId}' is not owned by this connection.",
            retryable: false
        );

    private static CommandErrorMessage CommandError(
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
            Error = new ControlErrorMessage
            {
                Kind = kind,
                Code = code,
                Message = message,
                Retryable = retryable,
                Details = new JObject(),
            },
        };
}
