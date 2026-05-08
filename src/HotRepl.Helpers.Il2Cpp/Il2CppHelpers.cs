using System;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace HotRepl.Helpers.Il2Cpp;

public static class Il2CppHelpers
{
    public static object[] FindObjects(string fullTypeName)
    {
        var wrapperType = ResolveManagedWrapperType(fullTypeName);
        var il2cppType = CreateIl2CppType(wrapperType);
        return Resources.FindObjectsOfTypeAll(il2cppType).Cast<object>().ToArray();
    }

    public static object DescribeType(string fullTypeName)
    {
        var wrapperType = ResolveManagedWrapperType(fullTypeName);
        return new
        {
            name = wrapperType.FullName,
            assembly = wrapperType.Assembly.GetName().Name,
            baseType = wrapperType.BaseType?.FullName,
            fields = wrapperType
                .GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(f => new { f.Name, type = f.FieldType.FullName })
                .ToArray(),
            properties = wrapperType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(p => new
                {
                    p.Name,
                    type = p.PropertyType.FullName,
                    p.CanRead,
                    p.CanWrite,
                })
                .ToArray(),
        };
    }

    public static string SafeName(object value)
    {
        if (value == null)
            return string.Empty;

        try
        {
            var nameProperty = value
                .GetType()
                .GetProperty("name", BindingFlags.Public | BindingFlags.Instance);
            if (nameProperty?.GetValue(value) is string name && !string.IsNullOrWhiteSpace(name))
                return name;
        }
        catch { }

        try
        {
            return value.ToString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            return $"<ToString error: {ex.Message}>";
        }
    }

    public static object TryCast(object value, string fullTypeName)
    {
        if (value == null)
            return null;

        var wrapperType = ResolveManagedWrapperType(fullTypeName);
        var method = typeof(Il2CppHelpers)
            .GetMethod(nameof(TryCastGeneric), BindingFlags.NonPublic | BindingFlags.Static)
            .MakeGenericMethod(wrapperType);
        return method.Invoke(null, new[] { value });
    }

    private static object TryCastGeneric<T>(object value)
        where T : class
    {
        var method = value
            .GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m =>
                string.Equals(m.Name, "TryCast", StringComparison.Ordinal)
                && m.IsGenericMethodDefinition
                && m.GetParameters().Length == 0
            );
        if (method == null)
            return value as T;
        return method.MakeGenericMethod(typeof(T)).Invoke(value, Array.Empty<object>());
    }

    private static Type ResolveManagedWrapperType(string fullTypeName)
    {
        var normalized = fullTypeName.StartsWith("Il2Cpp", StringComparison.Ordinal)
            ? fullTypeName
            : "Il2Cpp." + fullTypeName;

        var type = AppDomain
            .CurrentDomain.GetAssemblies()
            .Select(asm => asm.GetType(normalized, throwOnError: false))
            .FirstOrDefault(t => t != null);

        if (type == null)
            throw new InvalidOperationException(
                $"Could not resolve IL2CPP wrapper type '{normalized}'."
            );

        return type;
    }

    private static Il2CppSystem.Type CreateIl2CppType(Type wrapperType)
    {
        var method = typeof(Il2CppType)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m =>
                string.Equals(m.Name, nameof(Il2CppType.Of), StringComparison.Ordinal)
                && m.IsGenericMethodDefinition
                && m.GetParameters().Length == 0
            );
        return (Il2CppSystem.Type)
            method.MakeGenericMethod(wrapperType).Invoke(null, Array.Empty<object>());
    }
}
