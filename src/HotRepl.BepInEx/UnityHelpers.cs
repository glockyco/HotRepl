using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace HotRepl.BepInEx.Helpers;

/// <summary>
/// Unity-specific helper functions available in every REPL session as <c>UnityHelpers.*</c>.
///
/// ScreenCapture and ImageConversion live in optional Unity modules that may not be
/// present in the lib/ directory at build time. They are accessed via reflection so
/// this assembly compiles without those module DLLs. At runtime in the game all modules
/// are loaded and the reflection calls succeed.
/// </summary>
public static class UnityHelpers
{
    // Cached reflection targets — resolved once on first use.
    private static readonly Lazy<(MethodInfo capture, MethodInfo encode)> _screenshotMethods =
        new(ResolveScreenshotMethods);

    // ── Public helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Captures a screenshot and saves it as a PNG file.
    /// </summary>
    /// <param name="path">
    /// Destination file path. Defaults to <c>Application.temporaryCachePath/hotrepl_screenshot.png</c>
    /// when null.
    /// </param>
    /// <returns>The absolute path of the saved file.</returns>
    public static string Screenshot(string? path = null)
    {
        var tex = CaptureTexture();
        try
        {
            var png = EncodeToPng(tex);
            path ??= Path.Combine(Application.temporaryCachePath, "hotrepl_screenshot.png");
            File.WriteAllBytes(path, png);
            return path;
        }
        finally
        {
            UnityEngine.Object.Destroy(tex);
        }
    }

    /// <summary>Captures a screenshot and returns it as a base64-encoded PNG string.</summary>
    public static string ScreenshotBase64()
    {
        var tex = CaptureTexture();
        try
        { return Convert.ToBase64String(EncodeToPng(tex)); }
        finally { UnityEngine.Object.Destroy(tex); }
    }

    /// <summary>
    /// Traverses the active scene hierarchy and returns a filtered object tree.
    /// </summary>
    /// <param name="filter">Case-insensitive name substring filter. null = all objects.</param>
    /// <param name="layer">Layer name filter. null = all layers.</param>
    /// <param name="depth">Maximum traversal depth.</param>
    /// <param name="maxResults">Maximum number of nodes returned.</param>
    public static object SceneGraph(string? filter = null, string? layer = null, int depth = 3, int maxResults = 200)
    {
        var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
        var results = new System.Collections.Generic.List<object>();
        int count = 0;

        foreach (var root in roots)
        {
            if (count >= maxResults)
                break;
            var node = TraverseGO(root, filter, layer, depth, maxResults, ref count);
            if (node != null)
                results.Add(node);
        }

        return results;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static Texture2D CaptureTexture()
    {
        var (capture, _) = _screenshotMethods.Value;
        return (Texture2D)capture.Invoke(null, null)!;
    }

    private static byte[] EncodeToPng(Texture2D tex)
    {
        var (_, encode) = _screenshotMethods.Value;
        return (byte[])encode.Invoke(null, new object[] { tex })!;
    }

    private static (MethodInfo capture, MethodInfo encode) ResolveScreenshotMethods()
    {
        // UnityEngine.ScreenCapture.CaptureScreenshotAsTexture()
        var screenCapture = Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule")
                         ?? Type.GetType("UnityEngine.ScreenCapture, UnityEngine");
        var captureMethod = screenCapture?.GetMethod("CaptureScreenshotAsTexture",
            BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);

        // UnityEngine.ImageConversion.EncodeToPNG(Texture2D)
        var imageConversion = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")
                           ?? Type.GetType("UnityEngine.ImageConversion, UnityEngine");
        var encodeMethod = imageConversion?.GetMethod("EncodeToPNG",
            BindingFlags.Public | BindingFlags.Static);

        if (captureMethod == null || encodeMethod == null)
            throw new InvalidOperationException(
                "Screenshot helpers require UnityEngine.ScreenCaptureModule and UnityEngine.ImageConversionModule " +
                "to be loaded. These are available in the running game but may be absent in test environments.");

        return (captureMethod, encodeMethod);
    }

    private static System.Collections.Generic.Dictionary<string, object>? TraverseGO(
        GameObject go, string? filter, string? layer,
        int depth, int maxResults, ref int count)
    {
        if (count >= maxResults || depth < 0)
            return null;

        var goLayer = LayerMask.LayerToName(go.layer);
        bool nameMatch = filter == null || go.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        bool layerMatch = layer == null || string.Equals(goLayer, layer, StringComparison.OrdinalIgnoreCase);

        var children = new System.Collections.Generic.List<object>();
        if (depth > 0)
        {
            for (int i = 0; i < go.transform.childCount; i++)
            {
                if (count >= maxResults)
                    break;
                var child = TraverseGO(go.transform.GetChild(i).gameObject, filter, layer, depth - 1, maxResults, ref count);
                if (child != null)
                    children.Add(child);
            }
        }

        bool selfMatch = nameMatch && layerMatch;
        if (!selfMatch && children.Count == 0)
            return null;

        count++;
        return new System.Collections.Generic.Dictionary<string, object>
        {
            ["name"] = go.name,
            ["layer"] = goLayer,
            ["active"] = go.activeInHierarchy,
            ["components"] = Array.ConvertAll(
                go.GetComponents<Component>(),
                c => c != null ? (object)c.GetType().Name : "<null>"),
            ["children"] = children,
        };
    }
}
