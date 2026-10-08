using System;
using System.Linq;
using System.Reflection;

namespace HotRepl.Helpers;

/// <summary>
/// Formats a helper class's public static methods for the handshake
/// <c>helpers</c> field. Hosts use it for <see cref="IReplHost.AdditionalHelperSignatures"/>;
/// it is public because a loader may rename a host assembly at load time, which
/// defeats <c>InternalsVisibleTo</c>.
/// </summary>
public static class HelperSignatures
{
    /// <summary>Signatures such as <c>Int32 Count(String name)</c> of <paramref name="type"/>'s own public static methods.</summary>
    public static string[] For(Type type)
    {
        if (type == null)
            throw new ArgumentNullException(nameof(type));
        return type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m =>
                m.DeclaringType == type
                && !m.IsSpecialName
                && !m.Name.StartsWith("__", StringComparison.Ordinal)
            )
            .Select(Format)
            .ToArray();
    }

    private static string Format(MethodInfo m)
    {
        var ps = m.GetParameters();
        var pstr = string.Join(
            ", ",
            Array.ConvertAll(
                ps,
                p =>
                {
                    var part = $"{p.ParameterType.Name} {p.Name}";
                    if (p.HasDefaultValue)
                        part += $" = {p.DefaultValue ?? "null"}";
                    return part;
                }
            )
        );
        return $"{m.ReturnType.Name} {m.Name}({pstr})";
    }
}
