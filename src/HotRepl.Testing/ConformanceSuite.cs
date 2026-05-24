using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Sdk;

namespace HotRepl.Testing;

/// <summary>Basic SDK conformance checks for a connected HotRepl session.</summary>
public static class ConformanceSuite
{
    /// <summary>Run all conformance checks supported by this package.</summary>
    public static async Task<ConformanceResult> RunAllAsync(
        HotReplSession session,
        ConformanceOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        options ??= new ConformanceOptions();
        var checks = new List<ConformanceCheck>();
        var commands = await session.ListCommandsAsync(cancellationToken).ConfigureAwait(false);
        checks.Add(
            commands.Count > 0
                ? ConformanceCheck.Pass("commands_list")
                : ConformanceCheck.Fail("commands_list", "No commands returned.")
        );

        if (options.DescribeEveryCommand)
        {
            foreach (var command in commands)
            {
                var descriptor = await session
                    .DescribeCommandAsync(command.Name, cancellationToken)
                    .ConfigureAwait(false);
                checks.Add(
                    string.Equals(descriptor.Name, command.Name, StringComparison.Ordinal)
                        ? ConformanceCheck.Pass($"command_describe:{command.Name}")
                        : ConformanceCheck.Fail(
                            $"command_describe:{command.Name}",
                            "Descriptor name did not match catalog entry."
                        )
                );
            }
        }

        return new ConformanceResult(checks);
    }
}
