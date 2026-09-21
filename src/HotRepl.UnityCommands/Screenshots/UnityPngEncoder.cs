#nullable disable

using System;
using System.Reflection;

namespace HotRepl.UnityCommands.Screenshots;

internal static class UnityPngEncoder
{
    private static readonly Lazy<MethodInfo> InstanceEncodeToPngMethod = new(
        ResolveInstanceEncodeToPng
    );

    private static readonly Lazy<MethodInfo> StaticEncodeToPngMethod = new(
        ResolveStaticEncodeToPng
    );

    public static byte[] Encode(UnityEngine.Texture2D texture)
    {
        var instanceMethod = InstanceEncodeToPngMethod.Value;
        if (instanceMethod != null)
        {
            return ToManagedBytes(instanceMethod.Invoke(texture, Array.Empty<object>()));
        }

        var staticMethod = StaticEncodeToPngMethod.Value;
        return ToManagedBytes(staticMethod?.Invoke(null, new object[] { texture }));
    }

    private static byte[] ToManagedBytes(object value)
    {
        if (value is null)
        {
            return null;
        }
        if (value is byte[] bytes)
        {
            return bytes;
        }
        if (value is Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> il2CppBytes)
        {
            var managedBytes = new byte[il2CppBytes.Length];
            for (var index = 0; index < managedBytes.Length; index++)
            {
                managedBytes[index] = il2CppBytes[index];
            }
            return managedBytes;
        }

        var type = value.GetType();
        var lengthProperty = type.GetProperty(
            "Length",
            BindingFlags.Public | BindingFlags.Instance
        );
        var itemProperty = type.GetProperty("Item", BindingFlags.Public | BindingFlags.Instance);
        if (lengthProperty?.GetValue(value) is not int length || itemProperty is null)
        {
            return null;
        }

        var result = new byte[length];
        for (var index = 0; index < length; index++)
        {
            if (itemProperty.GetValue(value, new object[] { index }) is not byte element)
            {
                return null;
            }
            result[index] = element;
        }
        return result;
    }

    private static MethodInfo ResolveInstanceEncodeToPng() =>
        typeof(UnityEngine.Texture2D).GetMethod(
            "EncodeToPNG",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            Type.EmptyTypes,
            null
        );

    private static MethodInfo ResolveStaticEncodeToPng()
    {
        var imageConversion =
            Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")
            ?? Type.GetType("UnityEngine.ImageConversion, UnityEngine");
        if (imageConversion is null)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                imageConversion = assembly.GetType("UnityEngine.ImageConversion", false);
                if (imageConversion != null)
                {
                    break;
                }
            }
        }

        return imageConversion?.GetMethod(
            "EncodeToPNG",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(UnityEngine.Texture2D) },
            null
        );
    }
}
