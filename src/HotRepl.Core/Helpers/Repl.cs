using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HotRepl.Helpers;

/// <summary>
/// Statically injected helper class available in every REPL session as <c>Repl.*</c>.
/// Platform-agnostic (no BepInEx or UnityEngine references at compile time).
/// Unity-specific helpers (Screenshot, SceneGraph) are provided by HotRepl.BepInEx.
///
/// Lifecycle: the engine calls Initialize() once after constructing the evaluator
/// and again after every Reset(). All public methods are safe to call from eval code.
/// __RecordEntry is internal — only the engine calls it directly; user code cannot reach it.
/// </summary>
public static class Repl
{
    private static HistoryTracker _history = null!;
    private static int _maxEnumerableElements = 100;

    /// <summary>
    /// Binds the static helpers to the engine's runtime services.
    /// Must be called before any eval is processed.
    /// </summary>
    internal static void Initialize(HistoryTracker history, int maxEnumerableElements)
    {
        _history = history;
        _maxEnumerableElements = maxEnumerableElements;
    }

    // ── Public helpers ────────────────────────────────────────────────────────

    /// <summary>Returns signatures of all available public helper methods.</summary>
    public static string[] Help()
    {
        return typeof(Repl)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.DeclaringType == typeof(Repl) && !m.IsSpecialName)
            .Select(FormatSignature)
            .ToArray();
    }

    /// <summary>Returns the <paramref name="limit"/> most recent eval history entries.</summary>
    public static object History(int limit = 20)
    {
        return _history.GetRecent(limit)
            .Select(e => new Dictionary<string, object?>
            {
                ["code"] = e.Code,
                ["value"] = e.Value,
                ["error"] = e.Error,
                ["timestamp"] = e.Timestamp,
            })
            .ToArray();
    }

    /// <summary>
    /// Deeply inspects <paramref name="obj"/> via reflection, returning a
    /// dictionary tree. Handles circular references and depth limits.
    /// </summary>
    public static object? Inspect(object? obj, int depth = 2, int maxChildren = 50)
        => InspectCore(obj, depth, maxChildren, new HashSet<object>(ReferenceComparer.Instance));

    /// <summary>Returns type metadata: base type, interfaces, properties, fields, methods.</summary>
    public static object Describe(Type type)
    {
        const BindingFlags flags =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        return new Dictionary<string, object?>
        {
            ["type"] = type.FullName ?? type.Name,
            ["baseType"] = type.BaseType?.FullName ?? type.BaseType?.Name,
            ["interfaces"] = type.GetInterfaces().Select(i => i.FullName ?? i.Name).ToArray(),
            ["properties"] = type.GetProperties(flags).Select(p => new Dictionary<string, object>
            {
                ["name"] = p.Name,
                ["type"] = p.PropertyType.FullName ?? p.PropertyType.Name,
                ["canRead"] = p.CanRead,
                ["canWrite"] = p.CanWrite,
            }).ToArray(),
            ["fields"] = type.GetFields(flags).Select(f => new Dictionary<string, object>
            {
                ["name"] = f.Name,
                ["type"] = f.FieldType.FullName ?? f.FieldType.Name,
                ["isPublic"] = f.IsPublic,
            }).ToArray(),
            ["methods"] = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.Name != "Equals" && m.Name != "GetHashCode" && m.Name != "GetType" && m.Name != "ToString")
                .Select(m => new Dictionary<string, object>
                {
                    ["name"] = m.Name,
                    ["returnType"] = m.ReturnType.FullName ?? m.ReturnType.Name,
                    ["parameters"] = m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}").ToArray(),
                }).ToArray(),
        };
    }

    // ── Engine-internal ───────────────────────────────────────────────────────

    /// <summary>
    /// Records one eval result into the history ring buffer.
    /// Called directly by the engine (not via Evaluate()) — no evaluator re-entry.
    /// </summary>
    internal static void __RecordEntry(string code, string? serializedValue, string? errorMessage)
        => _history?.RecordEntry(code, serializedValue, errorMessage);

    // ── Private helpers ───────────────────────────────────────────────────────

    private static object? InspectCore(object? obj, int depth, int maxChildren, HashSet<object> visited)
    {
        if (obj == null)
            return null;

        var type = obj.GetType();

        // Primitives, strings, enums — return as-is.
        if (type.IsPrimitive || type.IsEnum || obj is string || obj is decimal)
            return obj;

        // Circular reference guard (reference types only).
        if (!type.IsValueType && !visited.Add(obj))
            return new Dictionary<string, object?> { ["_type"] = type.FullName, ["_circular"] = true };

        if (depth <= 0)
            return new Dictionary<string, object?> { ["_type"] = type.FullName, ["_truncated"] = true };

        var result = new Dictionary<string, object?> { ["_type"] = type.FullName };

        try
        { result["_value"] = obj.ToString(); }
        catch (Exception ex) { result["_value"] = $"<ToString error: {ex.Message}>"; }

        // IEnumerable — enumerate up to maxChildren.
        if (obj is IEnumerable enumerable && obj is not string)
        {
            var items = new List<object?>();
            int count = 0;
            foreach (var item in enumerable)
            {
                if (count++ >= maxChildren)
                    break;
                items.Add(InspectCore(item, depth - 1, maxChildren, visited));
            }
            result["_items"] = items;
            result["_count"] = count;
            return result;
        }

        // Reflect public instance properties.
        int childCount = 0;
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (childCount >= maxChildren)
                break;
            if (prop.GetIndexParameters().Length > 0)
                continue;
            try
            {
                result[prop.Name] = InspectCore(prop.GetValue(obj, null), depth - 1, maxChildren, visited);
                childCount++;
            }
            catch (Exception ex)
            {
                result[prop.Name] = new Dictionary<string, object?> { ["_error"] = ex.Message };
                childCount++;
            }
        }

        // Reflect public instance fields (skip if property already covered the name).
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (childCount >= maxChildren)
                break;
            if (result.ContainsKey(field.Name))
                continue;
            try
            {
                result[field.Name] = InspectCore(field.GetValue(obj), depth - 1, maxChildren, visited);
                childCount++;
            }
            catch (Exception ex)
            {
                result[field.Name] = new Dictionary<string, object?> { ["_error"] = ex.Message };
                childCount++;
            }
        }

        return result;
    }

    private static string FormatSignature(MethodInfo m)
    {
        var ps = m.GetParameters();
        var pstr = string.Join(", ", Array.ConvertAll(ps, p =>
        {
            var part = $"{p.ParameterType.Name} {p.Name}";
            if (p.HasDefaultValue)
                part += $" = {p.DefaultValue ?? "null"}";
            return part;
        }));
        return $"{m.ReturnType.Name} {m.Name}({pstr})";
    }

    /// <summary>Reference-equality comparer for the circular-reference visited set.</summary>
    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
