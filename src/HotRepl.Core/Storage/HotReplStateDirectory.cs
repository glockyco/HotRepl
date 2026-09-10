using System;
using System.IO;
using System.Runtime.InteropServices;

namespace HotRepl.Storage;

/// <summary>
/// Resolves the per-user directories HotRepl writes outside the game folder:
/// instance discovery documents and command artifacts.
/// </summary>
internal static class HotReplStateDirectory
{
    /// <summary>
    /// Directory for <paramref name="leaf"/> under the platform's per-user state
    /// location, falling back to the temporary directory when the platform
    /// reports none.
    /// </summary>
    public static string Resolve(string leaf)
    {
        var xdgRuntime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(xdgRuntime))
            return Path.Combine(xdgRuntime, "hotrepl", leaf);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData
            );
            return Path.Combine(localAppData, "HotRepl", leaf);
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
            return Path.Combine(appData, "HotRepl", leaf);

        return Path.Combine(Path.GetTempPath(), "hotrepl", leaf);
    }
}
