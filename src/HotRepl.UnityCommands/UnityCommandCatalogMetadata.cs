using System.Collections.Generic;
using HotRepl.Control;

namespace HotRepl.UnityCommands;

internal static class UnityCommandCatalogMetadata
{
    public static UnityCommandCatalogMetadataEntry AppInfo { get; } =
        new(UnityCommandCatalogNames.AppInfo, ControlCommandKind.Sync, mutatesState: false);

    public static UnityCommandCatalogMetadataEntry GameObjectFind { get; } =
        new(UnityCommandCatalogNames.GameObjectFind, ControlCommandKind.Sync, mutatesState: false);

    public static UnityCommandCatalogMetadataEntry TimeSetScale { get; } =
        new(UnityCommandCatalogNames.TimeSetScale, ControlCommandKind.Sync, mutatesState: true);

    public static UnityCommandCatalogMetadataEntry ScreenshotCapture { get; } =
        new(
            UnityCommandCatalogNames.ScreenshotCapture,
            ControlCommandKind.Job,
            mutatesState: false
        );

    public static IReadOnlyList<UnityCommandCatalogMetadataEntry> Commands { get; } =
        new[] { AppInfo, GameObjectFind, TimeSetScale, ScreenshotCapture };

    public static IReadOnlyList<string> Names { get; } =
        new[] { AppInfo.Name, GameObjectFind.Name, TimeSetScale.Name, ScreenshotCapture.Name };
}
