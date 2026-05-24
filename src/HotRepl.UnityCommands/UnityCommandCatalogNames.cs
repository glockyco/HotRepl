using System.Collections.Generic;

namespace HotRepl.UnityCommands;

/// <summary>Stable command names exported by the first-party UnityCommands plugin.</summary>
public static class UnityCommandCatalogNames
{
    /// <summary>Application/runtime metadata command.</summary>
    public const string AppInfo = "unity.app.info";

    /// <summary>GameObject lookup command.</summary>
    public const string GameObjectFind = "unity.gameobject.find";

    /// <summary>Time.timeScale mutation command.</summary>
    public const string TimeSetScale = "unity.time.set_scale";

    /// <summary>Screenshot artifact capture command.</summary>
    public const string ScreenshotCapture = "unity.screenshot.capture";

    /// <summary>Command names in registration order.</summary>
    public static IReadOnlyList<string> Names { get; } =
        new[] { AppInfo, GameObjectFind, TimeSetScale, ScreenshotCapture };
}
