using System;
using System.Linq;
using HotRepl.Evaluator;
using HotRepl.Protocol;
using HotRepl.Protocol.Serialization;

namespace HotRepl.Server;

/// <summary>Builds the v2 protocol handshake from runtime capabilities.</summary>
internal static class RuntimeHandshakeFactory
{
    public static string Serialize(
        ReplConfig config,
        HostInfo host,
        EvaluatorCapabilities evaluator,
        string[] availableEvaluators,
        string[] defaultUsings,
        string[] helpers
    ) =>
        ProtocolMessageSerializer.Serialize(
            Create(config, host, evaluator, availableEvaluators, defaultUsings, helpers)
        );

    public static HandshakeMessage Create(
        ReplConfig config,
        HostInfo host,
        EvaluatorCapabilities evaluator,
        string[] availableEvaluators,
        string[] defaultUsings,
        string[] helpers
    ) =>
        new()
        {
            ProtocolVersion = 2,
            Host = new HostDescriptor
            {
                Name = host.Name,
                Version = host.Version,
                Platform = host.Platform,
            },
            Evaluator = new EvaluatorDescriptor
            {
                Name = evaluator.Name,
                LanguageVersion = evaluator.LanguageVersion,
                PersistentState = evaluator.SupportsPersistentState,
                SupportsCompletion = evaluator.SupportsCompletion,
                Cancellation = ToCancellation(evaluator.TimeoutMode),
            },
            AvailableEvaluators = availableEvaluators.ToArray(),
            DefaultUsings = defaultUsings.ToArray(),
            Helpers = helpers.ToArray(),
            Control = new ControlCapabilities
            {
                Supported = true,
                CommandsListChanged = false,
                SchemaValidation = false,
            },
            Limits = new RuntimeLimits
            {
                MaxMessageBytes = config.MaxMessageBytes,
                MaxQueuedCommands = config.MaxQueuedCommands,
                MaxResultLength = config.MaxResultLength,
                MaxEnumerableElements = config.MaxEnumerableElements,
                DefaultEvalTimeoutMs = config.DefaultTimeoutMs,
                MaxJobConcurrency = Math.Max(1, config.MaxJobConcurrency),
            },
            Enforces = EnforcedLimit.Defaults,
        };

    private static string ToCancellation(TimeoutMode timeoutMode) =>
        timeoutMode switch
        {
            TimeoutMode.HardAbort => "hardAbort",
            TimeoutMode.Cooperative => "cooperative",
            TimeoutMode.None => "unsupported",
            _ => throw new ArgumentOutOfRangeException(nameof(timeoutMode), timeoutMode, null),
        };
}
