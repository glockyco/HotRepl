using System;
using System.Linq;
using HotRepl.Control;
using HotRepl.UnityCommands;
using Xunit;

namespace HotRepl.Tests.Unit;

public sealed class UnityCommandsCatalogTests
{
    [Fact]
    public void Names_AreTheExpectedFourUnityCommands()
    {
        Assert.Equal(
            new[]
            {
                "unity.app.info",
                "unity.gameobject.find",
                "unity.time.set_scale",
                "unity.screenshot.capture",
            },
            UnityCommandCatalogNames.Names
        );
    }

    [Fact]
    public void Metadata_AdvertisesRuntimeSafetyForEveryCommand()
    {
        var descriptors = UnityCommandCatalogMetadata.Commands.ToDictionary(
            command => command.Name,
            StringComparer.Ordinal
        );
        Assert.Equal(UnityCommandCatalogNames.Names, descriptors.Keys);

        Assert.Equal(
            ControlCommandKind.Job,
            descriptors[UnityCommandCatalogNames.ScreenshotCapture].Kind
        );
        Assert.False(descriptors[UnityCommandCatalogNames.ScreenshotCapture].MutatesState);
        Assert.True(descriptors[UnityCommandCatalogNames.TimeSetScale].MutatesState);
    }
}
