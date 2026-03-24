using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace HotRepl.BepInEx.Helpers;

/// <summary>
/// Unity-specific helper functions available in every REPL session as <c>UnityHelpers.*</c>.
///
/// Screenshot methods use Camera.Render() into a RenderTexture for synchronous capture
/// during eval. This avoids the frame-timing issues of ScreenCapture.CaptureScreenshot*
/// which only produce output after end-of-frame rendering — too late for an eval call
/// that runs during Update()/Tick().
///
/// ImageConversion is accessed via reflection because UnityEngine.ImageConversionModule
/// may not be present at build time. At runtime in the game all modules are loaded.
/// </summary>
public static class UnityHelpers
{
    // Cached reflection target — resolved once on first use.
    private static readonly Lazy<MethodInfo> _encodeToPng = new(ResolveEncodeToPng);

    // ── Public helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Captures a screenshot of the current camera view and saves it as a PNG file.
    /// Uses Camera.Render() for synchronous capture — works reliably from eval.
    /// </summary>
    /// <param name="path">
    /// Destination file path. Defaults to <c>Application.temporaryCachePath/hotrepl_screenshot.png</c>
    /// when null.
    /// </param>
    /// <returns>The absolute path of the saved file.</returns>
    public static string Screenshot(string? path = null)
    {
        path ??= Path.Combine(Application.temporaryCachePath, "hotrepl_screenshot.png");
        var png = CaptureAsPng();
        File.WriteAllBytes(path, png);
        return path;
    }

    /// <summary>Captures a screenshot and returns it as a base64-encoded PNG string.</summary>
    public static string ScreenshotBase64()
    {
        return Convert.ToBase64String(CaptureAsPng());
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

    // ── Private: screenshot capture ──────────────────────────────────────────

    /// <summary>
    /// Render the current camera view into a RenderTexture, read pixels, encode to PNG.
    /// Synchronous — no end-of-frame dependency.
    /// </summary>
    private static byte[] CaptureAsPng()
    {
        var cam = FindCamera();
        if (cam == null)
            throw new InvalidOperationException(
                "No active camera found. Tried Camera.main, 'MainCam', and Camera.allCameras.");

        int w = Screen.width;
        int h = Screen.height;
        var rt = new RenderTexture(w, h, 24);
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);

        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;
        try
        {
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            return EncodeToPng(tex);
        }
        finally
        {
            cam.targetTexture = prevTarget;
            RenderTexture.active = prevActive;
            UnityEngine.Object.Destroy(tex);
            UnityEngine.Object.Destroy(rt);
        }
    }

    /// <summary>
    /// Find the active rendering camera. Unity's Camera.main requires the
    /// "MainCamera" tag, which some games don't use. Falls back to name
    /// search and then to the first enabled camera.
    /// </summary>
    private static Camera? FindCamera()
    {
        // Standard Unity tag-based lookup.
        if (Camera.main != null)
            return Camera.main;

        // Common name-based fallback (e.g., Erenshor uses "MainCam" without the tag).
        var go = GameObject.Find("MainCam");
        if (go != null)
        {
            var cam = go.GetComponent<Camera>();
            if (cam != null && cam.enabled)
                return cam;
        }

        // Last resort: first enabled camera in the scene.
        foreach (var cam in Camera.allCameras)
        {
            if (cam != null && cam.enabled)
                return cam;
        }

        return null;
    }

    // ── Private: PNG encoding via reflection ─────────────────────────────────

    private static byte[] EncodeToPng(Texture2D tex)
    {
        return (byte[])_encodeToPng.Value.Invoke(null, new object[] { tex })!;
    }

    private static MethodInfo ResolveEncodeToPng()
    {
        var imageConversion = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")
                           ?? Type.GetType("UnityEngine.ImageConversion, UnityEngine");
        var method = imageConversion?.GetMethod("EncodeToPNG", BindingFlags.Public | BindingFlags.Static);

        if (method == null)
            throw new InvalidOperationException(
                "Screenshot helpers require UnityEngine.ImageConversionModule to be loaded. " +
                "This is available in the running game but may be absent in test environments.");

        return method;
    }

    // ── Private: scene graph traversal ───────────────────────────────────────

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
