namespace HotRepl.Helpers;

/// <summary>
/// Contains the C# source code for helper functions injected into the REPL
/// environment. These become available as <c>HotRepl.MethodName()</c> in
/// evaluated code.
/// </summary>
/// <remarks>
/// New helpers are added here as methods on the <c>HotRepl</c> class.
/// The source is evaluated once during evaluator initialization, so helpers
/// have full access to all referenced assemblies and default usings.
/// </remarks>
internal static class ReplHelpers
{
    /// <summary>
    /// C# source code defining the HotRepl static helper class.
    /// Evaluated into the REPL on startup and after each reset.
    /// </summary>
    public const string Source = @"
public static class HotRepl
{
    // --- Helpers are added here by follow-up tasks ---
    // Each helper is a static method returning a serialization-friendly type
    // (string, Dictionary, List, primitive) so ResultSerializer handles it.

    /// <summary>Returns the list of available helper methods.</summary>
    public static string[] Help()
    {
        return typeof(HotRepl)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(m => m.DeclaringType == typeof(HotRepl) && m.Name != ""GetType"")
            .Select(m =>
            {
                var ps = m.GetParameters();
                var pstr = string.Join("", "",
                    ps.Select(p => p.ParameterType.Name + "" "" + p.Name +
                        (p.HasDefaultValue ? "" = "" + (p.DefaultValue ?? ""null"") : """")));
                return m.Name + ""("" + pstr + "") -> "" + m.ReturnType.Name;
            })
            .ToArray();
    }

    /// <summary>Captures a screenshot and saves it as PNG.</summary>
    /// <param name=""path"">File path. Defaults to a temp file if null.</param>
    /// <returns>The absolute file path of the saved screenshot.</returns>
    public static string Screenshot(string path = null)
    {
        var tex = UnityEngine.ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            var pngBytes = UnityEngine.ImageConversion.EncodeToPNG(tex);
            if (path == null)
                path = UnityEngine.Application.temporaryCachePath + ""/hotrepl_screenshot.png"";
            System.IO.File.WriteAllBytes(path, pngBytes);
            return path;
        }
        finally
        {
            UnityEngine.Object.Destroy(tex);
        }
    }

    /// <summary>Captures a screenshot and returns it as a base64-encoded PNG.</summary>
    public static string ScreenshotBase64()
    {
        var tex = UnityEngine.ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            var pngBytes = UnityEngine.ImageConversion.EncodeToPNG(tex);
            return System.Convert.ToBase64String(pngBytes);
        }
        finally
        {
            UnityEngine.Object.Destroy(tex);
        }
    }

    /// <summary>Traverses the active scene hierarchy and returns a filtered tree.</summary>
    public static object SceneGraph(string filter = null, string layer = null, int depth = 3, int maxResults = 200)
    {
        var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
        var count = 0;
        var results = new System.Collections.Generic.List<object>();
        foreach (var root in roots)
        {
            if (count >= maxResults) break;
            var node = _TraverseGO(root, filter, layer, depth, maxResults, ref count);
            if (node != null) results.Add(node);
        }
        return results;
    }

    private static System.Collections.Generic.Dictionary<string, object> _TraverseGO(
        UnityEngine.GameObject go, string filter, string layer, int depth, int maxResults, ref int count)
    {
        if (count >= maxResults || depth < 0) return null;

        var goLayer = UnityEngine.LayerMask.LayerToName(go.layer);
        var nameMatch = filter == null || go.name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0;
        var layerMatch = layer == null || string.Equals(goLayer, layer, System.StringComparison.OrdinalIgnoreCase);

        // Traverse children regardless — a descendant may match even if this node doesn't
        var childNodes = new System.Collections.Generic.List<object>();
        if (depth > 0)
        {
            for (int i = 0; i < go.transform.childCount; i++)
            {
                if (count >= maxResults) break;
                var child = _TraverseGO(go.transform.GetChild(i).gameObject, filter, layer, depth - 1, maxResults, ref count);
                if (child != null) childNodes.Add(child);
            }
        }

        // Include this node if it matches, or if any descendant matched
        bool selfMatch = nameMatch && layerMatch;
        if (!selfMatch && childNodes.Count == 0) return null;

        count++;
        var dict = new System.Collections.Generic.Dictionary<string, object>();
        dict[""name""] = go.name;
        dict[""layer""] = goLayer;
        dict[""active""] = go.activeInHierarchy;
        dict[""components""] = go.GetComponents<UnityEngine.Component>()
            .Where(c => c != null)
            .Select(c => c.GetType().Name)
            .ToArray();
        dict[""children""] = childNodes;
        return dict;
    }
}
";

    /// <summary>
    /// Method signatures advertised in the handshake message.
    /// Updated manually when helpers are added.
    /// </summary>
    public static readonly string[] AdvertisedHelpers = new[]
    {
        "HotRepl.Help() -> string[]",
        "HotRepl.Screenshot(path = null) -> string",
        "HotRepl.ScreenshotBase64() -> string",
        "HotRepl.SceneGraph(filter = null, layer = null, depth = 3, maxResults = 200) -> Object",
    };
}
