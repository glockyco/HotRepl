using System;
using System.Collections.Generic;

namespace HotRepl;

/// <summary>Security checks for WebSocket bind and control-plane authentication configuration.</summary>
public static class ReplConfigExposurePolicy
{
    /// <summary>Returns warnings for configuration that exposes HotRepl beyond loopback.</summary>
    public static ExposureValidationResult Validate(ReplConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        var warnings = new List<string>();
        var loopbackOnly = IsLoopback(config.BindHost);
        if (!loopbackOnly && string.IsNullOrWhiteSpace(config.ControlAuthToken))
        {
            warnings.Add(
                $"HotRepl BindHost '{config.BindHost}' is reachable beyond loopback and ControlAuthToken is empty. This exposes the REPL/control socket to any client that can reach the host."
            );
        }

        return new ExposureValidationResult(
            loopbackOnly && warnings.Count == 0,
            warnings.ToArray()
        );
    }

    /// <summary>Requires control-plane authentication when a token is configured.</summary>
    public static void ApplyControlAuthToken(ReplConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));
        if (!string.IsNullOrWhiteSpace(config.ControlAuthToken))
            config.RequireControlAuth = true;
    }

    private static bool IsLoopback(string? bindHost)
    {
        if (string.IsNullOrWhiteSpace(bindHost))
            return true;
        return string.Equals(bindHost, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(bindHost, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(bindHost, "::1", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Result of HotRepl network exposure validation.</summary>
public sealed record ExposureValidationResult(bool IsSafeDefault, IReadOnlyList<string> Warnings);
