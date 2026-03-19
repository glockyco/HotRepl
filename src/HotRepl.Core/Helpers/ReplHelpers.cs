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
}
";

    /// <summary>
    /// Method signatures advertised in the handshake message.
    /// Updated manually when helpers are added.
    /// </summary>
    public static readonly string[] AdvertisedHelpers = new[]
    {
        "HotRepl.Help() -> string[]",
    };
}
