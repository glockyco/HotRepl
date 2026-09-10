using System;
using System.IO;

namespace HotRepl.Tests;

/// <summary>Per-test artifact directories, so one test never reads another's files.</summary>
internal static class TestArtifacts
{
    public static string Directory() =>
        Path.Combine(
            Path.GetTempPath(),
            "hotrepl-tests",
            Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture)
        );
}
