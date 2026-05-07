using System;
using System.Linq;
using System.Threading;
using HotRepl.Control.Artifacts;
using HotRepl.Protocol;
using Newtonsoft.Json.Linq;

namespace HotRepl.Control;

/// <summary>Converts control-plane protocol messages into command handler calls and response messages.</summary>
internal sealed class ControlCommandRouter
{
    private readonly IControlCommandRegistry _registry;

    public ControlCommandRouter(IControlCommandRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public CommandDescribeResultMessage Describe(string id)
    {
        return new CommandDescribeResultMessage
        {
            Id = id,
            Commands = _registry.Describe().Select(ToMessage).ToArray(),
        };
    }

    public object Execute(CommandCallMessage message)
    {
        if (!_registry.TryGet(message.Name, out var handler))
        {
            return CommandError(message.Id, "unknown_command", "unknownCommand", $"Unknown control command '{message.Name}'.", retryable: false);
        }

        if (handler.Descriptor.Kind != ControlCommandKind.Synchronous)
        {
            return CommandError(message.Id, "unsupported_operation", "jobCommandRequiresJobProtocol", $"Command '{message.Name}' is not synchronous.", retryable: false);
        }

        try
        {
            var timeout = message.TimeoutMs > 0 ? TimeSpan.FromMilliseconds(message.TimeoutMs) : (TimeSpan?)null;
            var context = new ControlCommandContext(message.Id, message.LeaseId, message.IdempotencyKey, timeout);
            var result = handler.ExecuteAsync(context, message.Args, CancellationToken.None).AsTask().GetAwaiter().GetResult();
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
            return CommandError(message.Id, "internal", "handlerException", ex.Message, retryable: false);
        }
    }

    private static CommandDescriptorMessage ToMessage(ControlCommandDescriptor descriptor) => new()
    {
        Name = descriptor.Name,
        Version = descriptor.Version,
        Kind = descriptor.Kind == ControlCommandKind.Synchronous ? "sync" : "job",
        MutatesState = descriptor.MutatesState,
        ArgsSchema = descriptor.ArgsSchema,
        ResultSchema = descriptor.ResultSchema,
    };

    private static ArtifactRefMessage ToMessage(ArtifactRef artifact) => new()
    {
        LogicalName = artifact.LogicalName,
        Uri = artifact.Uri,
        Path = artifact.Path,
        ContentType = artifact.ContentType,
        ByteSize = artifact.ByteSize,
        Sha256 = artifact.Sha256,
        Finalized = artifact.Finalized,
    };

    private static ControlErrorMessage ToMessage(ControlCommandError error) => new()
    {
        Kind = error.Kind,
        Code = error.Code,
        Message = error.Message,
        Retryable = error.Retryable,
        Details = error.Details,
    };

    private static CommandErrorMessage CommandError(string id, string kind, string code, string message, bool retryable) => new()
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
