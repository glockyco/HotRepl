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
    private static readonly System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>> _history
        = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>();

    /// <summary>Engine-internal: records an eval result. Not intended for user code.</summary>
    public static void __RecordEntry(string codeB64, string valueB64, string errorB64)
    {
        _history.Add(new System.Collections.Generic.Dictionary<string, object>
        {
            {""code"", System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(codeB64))},
            {""value"", System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(valueB64))},
            {""error"", System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(errorB64))},
            {""timestamp"", System.DateTime.UtcNow.ToString(""o"")}
        });
        if (_history.Count > 100) _history.RemoveAt(0);
    }

    /// <summary>Returns recent eval history entries (most recent last).</summary>
    public static object History(int limit = 20)
    {
        return _history.Skip(System.Math.Max(0, _history.Count - limit)).ToList();
    }


    /// <summary>Returns the list of available helper methods.</summary>
    public static string[] Help()
    {
        return typeof(HotRepl)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(m => m.DeclaringType == typeof(HotRepl) && m.Name != ""GetType"" && !m.Name.StartsWith(""__""))
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

    /// <summary>Describes a Type: base, interfaces, properties, fields, methods.</summary>
    public static object Describe(System.Type type)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.DeclaredOnly;

        var properties = type.GetProperties(flags)
            .Select(p => new Dictionary<string, object>
            {
                { ""name"", p.Name },
                { ""type"", p.PropertyType.FullName ?? p.PropertyType.Name },
                { ""canRead"", p.CanRead },
                { ""canWrite"", p.CanWrite },
            })
            .ToArray();

        var fields = type.GetFields(flags)
            .Select(f => new Dictionary<string, object>
            {
                { ""name"", f.Name },
                { ""type"", f.FieldType.FullName ?? f.FieldType.Name },
                { ""isPublic"", f.IsPublic },
            })
            .ToArray();

        var skipNames = new System.Collections.Generic.HashSet<string>
            { ""Equals"", ""GetHashCode"", ""GetType"", ""ToString"" };

        var methods = type.GetMethods(System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && !skipNames.Contains(m.Name))
            .Select(m => new Dictionary<string, object>
            {
                { ""name"", m.Name },
                { ""returnType"", m.ReturnType.FullName ?? m.ReturnType.Name },
                { ""parameters"", m.GetParameters()
                    .Select(p => p.ParameterType.Name + "" "" + p.Name)
                    .ToArray() },
            })
            .ToArray();

        return new Dictionary<string, object>
        {
            { ""type"", type.FullName ?? type.Name },
            { ""baseType"", type.BaseType != null ? (type.BaseType.FullName ?? type.BaseType.Name) : null },
            { ""interfaces"", type.GetInterfaces().Select(i => i.FullName ?? i.Name).ToArray() },
            { ""properties"", properties },
            { ""fields"", fields },
            { ""methods"", methods },
        };
    }

    /// <summary>Deeply inspects an object via reflection, returning a Dictionary tree.</summary>
    public static object Inspect(object obj, int depth = 2, int maxChildren = 50)
    {
        return _InspectCore(obj, depth, maxChildren, new HashSet<object>(new _RefComparer()));
    }

    private static object _InspectCore(object obj, int depth, int maxChildren, HashSet<object> visited)
    {
        if (obj == null) return null;

        var type = obj.GetType();

        // Destroyed Unity objects: operator== returns true for null but the reference is non-null
        var uo = obj as UnityEngine.Object;
        if (uo != null && !uo)
        {
            return new Dictionary<string, object>
            {
                { ""_type"", type.FullName },
                { ""_destroyed"", true }
            };
        }

        // Primitives, strings, enums -- return directly
        if (type.IsPrimitive || type.IsEnum || obj is string || obj is decimal)
            return obj;

        // Circular reference guard (reference types only)
        if (!type.IsValueType)
        {
            if (!visited.Add(obj))
                return new Dictionary<string, object>
                {
                    { ""_type"", type.FullName },
                    { ""_circular"", true }
                };
        }

        // Depth limit
        if (depth <= 0)
            return new Dictionary<string, object>
            {
                { ""_type"", type.FullName },
                { ""_truncated"", true }
            };

        var result = new Dictionary<string, object>();
        result[""_type""] = type.FullName;

        try { result[""_value""] = obj.ToString(); }
        catch (System.Exception ex) { result[""_value""] = ""<ToString error: "" + ex.Message + "">""; }

        // GameObject specifics
        var go = obj as UnityEngine.GameObject;
        if (go != null)
        {
            result[""_active""] = go.activeSelf;
            result[""_layer""] = go.layer;
            var comps = go.GetComponents<UnityEngine.Component>();
            var compNames = new List<string>();
            foreach (var c in comps)
                compNames.Add(c != null ? c.GetType().FullName : ""<null>"");
            result[""_components""] = compNames;
            var children = new List<string>();
            for (int i = 0; i < go.transform.childCount && i < maxChildren; i++)
                children.Add(go.transform.GetChild(i).name);
            result[""_children""] = children;
        }

        // Component specifics
        var comp = obj as UnityEngine.Component;
        if (comp != null && go == null)
        {
            try { result[""_gameObject""] = comp.gameObject.name; }
            catch (System.Exception ex) { result[""_gameObject""] = new Dictionary<string, object> { { ""_error"", ex.Message } }; }
        }

        // Collections -- enumerate up to maxChildren elements
        var enumerable = obj as System.Collections.IEnumerable;
        if (enumerable != null && !(obj is string))
        {
            var items = new List<object>();
            int count = 0;
            foreach (var item in enumerable)
            {
                if (count >= maxChildren) break;
                items.Add(_InspectCore(item, depth - 1, maxChildren, visited));
                count++;
            }
            result[""_items""] = items;
            result[""_count""] = count;
            return result;
        }

        // Reflect public instance properties
        var props = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        int childCount = 0;
        foreach (var prop in props)
        {
            if (childCount >= maxChildren) break;
            if (prop.GetIndexParameters().Length > 0) continue; // skip indexers
            try
            {
                var val = prop.GetValue(obj, null);
                result[prop.Name] = _InspectCore(val, depth - 1, maxChildren, visited);
                childCount++;
            }
            catch (System.Exception ex)
            {
                result[prop.Name] = new Dictionary<string, object> { { ""_error"", ex.Message } };
                childCount++;
            }
        }

        // Reflect public instance fields
        var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        foreach (var field in fields)
        {
            if (childCount >= maxChildren) break;
            if (result.ContainsKey(field.Name)) continue; // property already covered it
            try
            {
                var val = field.GetValue(obj);
                result[field.Name] = _InspectCore(val, depth - 1, maxChildren, visited);
                childCount++;
            }
            catch (System.Exception ex)
            {
                result[field.Name] = new Dictionary<string, object> { { ""_error"", ex.Message } };
                childCount++;
            }
        }

        return result;
    }

    /// <summary>Reference equality comparer for the circular-reference visited set.</summary>
    private class _RefComparer : System.Collections.Generic.IEqualityComparer<object>
    {
        public new bool Equals(object x, object y) { return object.ReferenceEquals(x, y); }
        public int GetHashCode(object obj) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj); }
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
        "HotRepl.Describe(Type) -> object",
        "HotRepl.History(Int32 limit = 20) -> Object",
        "HotRepl.Inspect(object, depth?, maxChildren?) -> object",
    };
}
