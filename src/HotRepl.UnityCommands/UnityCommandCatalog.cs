using System;
using System.Collections;
using System.Collections.Generic;
using HotRepl.Control;
using HotRepl.UnityCommands.Commands;
using HotRepl.UnityCommands.Models;
using HotRepl.UnityCommands.Screenshots;

namespace HotRepl.UnityCommands;

/// <summary>Factory catalog shared by the BepInEx, MelonLoader, and Lunaris hosts.</summary>
public static class UnityCommandCatalog
{
    /// <summary>Command names in registration order.</summary>
    public static IReadOnlyList<string> Names => UnityCommandCatalogNames.Names;

    /// <summary>Registers enabled commands with end-of-frame screenshots; dispose on host unload.</summary>
    public static IDisposable Register(
        IControlCommandRegistry registry,
        Action<IEnumerator> startCoroutine,
        bool enabled,
        string disabledCsv,
        Action<string> log
    )
    {
        var factories = Build(new EndOfFrameUnityScreenshotCapturer(startCoroutine));
        return UnityCommandRegistrationSet.Register(
            Names,
            index => factories[index](registry),
            enabled,
            disabledCsv,
            log
        );
    }

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
