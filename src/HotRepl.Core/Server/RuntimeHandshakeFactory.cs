extern alias HotReplProtocolV2;

using System;
using System.Linq;
using HotRepl.Evaluator;
using ProtocolV2 = HotReplProtocolV2::HotRepl.Protocol;
using ProtocolMessageSerializer = HotReplProtocolV2::HotRepl.Protocol.Serialization.ProtocolMessageSerializer;

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
    ) => ProtocolMessageSerializer.Serialize(Create(config, host, evaluator, availableEvaluators, defaultUsings, helpers));

    public static ProtocolV2.HandshakeMessage Create(
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
            Host = new ProtocolV2.HostDescriptor
            {
                Name = host.Name,
                Version = host.Version,
                Platform = host.Platform,
            },
            Evaluator = new ProtocolV2.EvaluatorDescriptor
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
            Control = new ProtocolV2.ControlCapabilities
            {
                Supported = config.ControlPlaneEnabled,
                CommandsListChanged = false,
                SchemaValidation = config.SchemaValidation,
            },
            Limits = new ProtocolV2.RuntimeLimits
            {
                MaxMessageBytes = config.MaxMessageBytes,
                MaxQueuedCommands = config.MaxQueuedCommands,
                MaxResultLength = config.MaxResultLength,
                MaxEnumerableElements = config.MaxEnumerableElements,
                DefaultEvalTimeoutMs = config.DefaultTimeoutMs,
                MaxJobConcurrency = config.MaxJobConcurrency,
            },
            Enforces = ProtocolV2.EnforcedLimit.Defaults,
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
