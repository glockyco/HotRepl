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
            return instanceMethod.Invoke(texture, Array.Empty<object>()) as byte[];
        }

        var staticMethod = StaticEncodeToPngMethod.Value;
        return staticMethod?.Invoke(null, new object[] { texture }) as byte[];
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
        return imageConversion?.GetMethod(
            "EncodeToPNG",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(UnityEngine.Texture2D) },
            null
        );
    }
}
