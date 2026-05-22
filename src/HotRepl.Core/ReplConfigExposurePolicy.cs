using System;
using System.Collections.Generic;

namespace HotRepl;

/// <summary>Security checks for WebSocket bind configuration.</summary>
public static class ReplConfigExposurePolicy
{
    /// <summary>Returns warnings for configuration that exposes HotRepl beyond loopback.</summary>
    public static ExposureValidationResult Validate(ReplConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        var warnings = new List<string>();
        var loopbackOnly = IsLoopback(config.BindHost);
        if (!loopbackOnly)
        {
            warnings.Add(
                $"HotRepl BindHost '{config.BindHost}' is reachable beyond loopback. Protocol v2 has no auth or lease handshake; use loopback binding or an external trusted network boundary."
            );
        }

        return new ExposureValidationResult(
            loopbackOnly && warnings.Count == 0,
            warnings.ToArray()
        );
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
