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

        var hideIndex = source.IndexOf(
            "gameObject.hideFlags = HideFlags.HideAndDontSave;",
            StringComparison.Ordinal
        );
        var configIndex = source.IndexOf("var config = LoadConfig();", StringComparison.Ordinal);

        Assert.True(
            hideIndex >= 0,
            "ReplPlugin.Awake must hide the BepInEx manager GameObject so games cannot destroy HotRepl during scene cleanup."
        );
        Assert.True(configIndex >= 0, "Expected ReplPlugin.Awake to load configuration.");
        Assert.True(
            hideIndex < configIndex,
            "The manager GameObject must be hidden before HotRepl starts the WebSocket engine."
        );
    }

    [Fact]
    public void LoadConfig_AppliesEnvironmentAfterPluginSettings()
    {
        var source = File.ReadAllText(FindRepoFile("src/HotRepl.BepInEx/ReplPlugin.cs"));
        var artifactIndex = source.IndexOf(
            "config.ArtifactDirectory = artifactDirectory.Value.Trim();",
            StringComparison.Ordinal
        );
        var environmentIndex = source.IndexOf(
            "config.ApplyEnvironmentOverrides();",
            StringComparison.Ordinal
        );
        var validationIndex = source.IndexOf(
            "ReplConfigExposurePolicy.Validate(config)",
            StringComparison.Ordinal
        );
        Assert.True(artifactIndex >= 0);
        Assert.True(environmentIndex > artifactIndex);
        Assert.True(validationIndex > environmentIndex);
    }

    [Theory]
    [InlineData("src/HotRepl.BepInEx/ReplPlugin.cs", "UnityHelpers.Release(this);")]
    [InlineData("src/HotRepl.Host.MelonLoader/ReplMod.cs", "UnityHelpers.Release(_coroutineHost);")]
    public void LoaderUnload_ReleasesUnityCoroutineHost(string relativePath, string releaseCall)
    {
        var source = File.ReadAllText(FindRepoFile(relativePath));
        Assert.Contains(releaseCall, source, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_ReferencesProtocolAssemblyForRuntimeDeployment()
    {
        var project = File.ReadAllText(FindRepoFile("src/HotRepl.BepInEx/HotRepl.BepInEx.csproj"));

        Assert.Contains(
            "../HotRepl.Protocol/HotRepl.Protocol.csproj",
            project,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void ILRepack_KeepsProtocolAssemblySideBySide()
    {
        var targets = File.ReadAllText(FindRepoFile("src/HotRepl.BepInEx/ILRepack.targets"));

        Assert.Contains("$(OutputPath)HotRepl.Protocol.dll", targets, StringComparison.Ordinal);
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

        throw new FileNotFoundException(
            $"Could not find repository file '{relativePath}' from '{AppContext.BaseDirectory}'."
        );
    }
}
