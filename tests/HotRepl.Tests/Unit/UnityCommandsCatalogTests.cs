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
    public void Build_AdvertisesRuntimeSafetyMetadata()
    {
        var registry = new GlobalControlCommandRegistry();
        var registrations = UnityCommandCatalog
            .Build()
            .Select(factory => factory(registry))
            .ToArray();
        try
        {
            var descriptors = registry
                .Describe()
                .ToDictionary(descriptor => descriptor.Name, StringComparer.Ordinal);

            Assert.Equal(
                ControlCommandKind.Job,
                descriptors[UnityCommandCatalogNames.ScreenshotCapture].Kind
            );
            Assert.False(descriptors[UnityCommandCatalogNames.ScreenshotCapture].MutatesState);
            Assert.True(descriptors[UnityCommandCatalogNames.TimeSetScale].MutatesState);
        }
        finally
        {
            foreach (var registration in registrations)
            {
                registration.Dispose();
            }
        }
    }
}
