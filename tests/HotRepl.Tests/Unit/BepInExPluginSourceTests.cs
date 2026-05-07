using System;
using System.IO;
using Xunit;

namespace HotRepl.Tests.Unit;

public sealed class BepInExPluginSourceTests
{
    [Fact]
    public void Awake_HidesBepInExManagerBeforeStartingEngine()
    {
        var source = File.ReadAllText(FindRepoFile("src/HotRepl.BepInEx/ReplPlugin.cs"));

        var hideIndex = source.IndexOf("gameObject.hideFlags = HideFlags.HideAndDontSave;", StringComparison.Ordinal);
        var configIndex = source.IndexOf("var config = LoadConfig();", StringComparison.Ordinal);

        Assert.True(hideIndex >= 0, "ReplPlugin.Awake must hide the BepInEx manager GameObject so games cannot destroy HotRepl during scene cleanup.");
        Assert.True(configIndex >= 0, "Expected ReplPlugin.Awake to load configuration.");
        Assert.True(hideIndex < configIndex, "The manager GameObject must be hidden before HotRepl starts the WebSocket engine.");
    }

    private static string FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
