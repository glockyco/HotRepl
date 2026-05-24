using System;
using System.Collections.Generic;
using HotRepl.Control;
using HotRepl.UnityCommands.Commands;
using HotRepl.UnityCommands.Models;
using HotRepl.UnityCommands.Screenshots;

namespace HotRepl.UnityCommands;

/// <summary>Factory catalog shared by the BepInEx and MelonLoader UnityCommands loaders.</summary>
public static class UnityCommandCatalog
{
    /// <summary>Command names in registration order.</summary>
    public static IReadOnlyList<string> Names => UnityCommandCatalogNames.Names;

    /// <summary>Builds registration factories for every first-party Unity command.</summary>
    public static IReadOnlyList<RegistrationFactory> Build() =>
        Build(UnsupportedUnityScreenshotCapturer.Instance);

    internal static IReadOnlyList<RegistrationFactory> Build(
        IUnityScreenshotCapturer screenshotCapturer
    ) =>
        new RegistrationFactory[]
        {
            registry => registry.Register<EmptyArgs, UnityAppInfo>(new UnityAppInfoCommand()),
            registry =>
                registry.Register<UnityGameObjectFindArgs, UnityGameObjectFindResult>(
                    new UnityGameObjectFindCommand()
                ),
            registry =>
                registry.Register<UnitySetTimeScaleArgs, UnitySetTimeScaleResult>(
                    new UnityTimeSetScaleCommand()
                ),
            registry =>
                registry.Register<UnityScreenshotArgs, UnityScreenshotResult>(
                    new UnityScreenshotCommand(screenshotCapturer)
                ),
        };

    /// <summary>Registers one command and returns the disposable registration.</summary>
    public delegate IDisposable RegistrationFactory(IControlCommandRegistry registry);
}
