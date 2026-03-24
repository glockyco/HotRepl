using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace HotRepl.BepInEx.Helpers;

/// <summary>
/// Unity-specific helper functions available in every REPL session as <c>UnityHelpers.*</c>.
///
/// Screenshot uses WaitForEndOfFrame + ReadPixels to capture the full frame buffer
/// including all UI overlays, nameplates, and ImGui. The capture runs as a coroutine
/// on the plugin's MonoBehaviour — the eval returns the output path immediately and
/// the file appears after the current frame finishes rendering.
///
/// ImageConversion is accessed via reflection because UnityEngine.ImageConversionModule
/// may not be present at build time. At runtime in the game all modules are loaded.
/// </summary>
public static class UnityHelpers
{
    // Cached reflection target — resolved once on first use.
    private static readonly Lazy<MethodInfo> _encodeToPng = new(ResolveEncodeToPng);

    // MonoBehaviour host for coroutine execution. Set by ReplPlugin.Awake().
    private static MonoBehaviour? _coroutineHost;

    /// <summary>
    /// Provide the MonoBehaviour host for coroutine-based screenshot capture.
    /// Called once from ReplPlugin.Awake().
    /// </summary>
    internal static void Initialize(MonoBehaviour host)
    {
        _coroutineHost = host;
    }

    // ── Public helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Captures a full-frame screenshot including all UI overlays and saves it as PNG.
    /// The file is written asynchronously after the current frame renders.
    /// </summary>
    /// <param name="path">
    /// Destination file path. Defaults to <c>Application.temporaryCachePath/hotrepl_screenshot.png</c>
    /// when null.
    /// </param>
    /// <returns>The path where the file will be saved.</returns>
    public static string Screenshot(string? path = null)
    {
        if (_coroutineHost == null)
            throw new InvalidOperationException("UnityHelpers not initialized — no coroutine host.");

        path ??= Path.Combine(Application.temporaryCachePath, "hotrepl_screenshot.png");
        _coroutineHost.StartCoroutine(CaptureFullFrame(path));
        return path;
    }

    /// <summary>Captures a full-frame screenshot and returns it as a base64-encoded PNG string.</summary>
    /// <remarks>
    /// Unlike <see cref="Screenshot"/>, this must wait for the frame to render before
    /// encoding. It blocks via a synchronous Camera.Render fallback since coroutine
    /// results cannot be returned to the eval caller.
    /// </remarks>
    public static string ScreenshotBase64()
    {
        var png = CaptureViaCamera();
        return Convert.ToBase64String(png);
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

    // ── Private: full-frame capture via coroutine ────────────────────────────

    /// <summary>
    /// Coroutine that waits for end-of-frame rendering to complete, then reads
    /// the full frame buffer (including UI, nameplates, overlays) into a PNG.
    /// </summary>
    private static IEnumerator CaptureFullFrame(string path)
    {
        // Skip one frame so all systems (markers, ImGui, nameplates) have
        // a full frame to update and render. Then capture at the end of the
        // following frame, after all rendering passes have completed.
        yield return null;
        yield return new WaitForEndOfFrame();

        var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        try
        {
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, EncodeToPng(tex));
        }
        finally
        {
            UnityEngine.Object.Destroy(tex);
        }
    }

    // ── Private: synchronous camera capture (3D only, no UI) ─────────────────

    /// <summary>
    /// Synchronous fallback for ScreenshotBase64: renders via Camera.Render()
    /// into a RenderTexture. Captures the 3D scene but not UI overlays.
    /// </summary>
    private static byte[] CaptureViaCamera()
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
    /// "MainCamera" tag, which some games don't use.
    /// </summary>
    private static Camera? FindCamera()
    {
        if (Camera.main != null)
            return Camera.main;

        var go = GameObject.Find("MainCam");
        if (go != null)
        {
            var cam = go.GetComponent<Camera>();
            if (cam != null && cam.enabled)
                return cam;
        }

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
