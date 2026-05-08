using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace HotRepl.Tests.Unit;

public class RoslynPackageCompatibilityTests
{
    [Fact]
    public void RoslynScriptingPackage_UsesMelonLoaderNet6CompatibleVersion()
    {
        // Versions live in Directory.Packages.props under Central Package Management.
        // The Roslyn scripting version is pinned to a .NET 6-compatible release so
        // the MelonLoader host can load it at runtime.
        var packages = XDocument.Load(ProjectPath("Directory.Packages.props"));
        var entry = packages
            .Descendants("PackageVersion")
            .Single(x =>
                (string?)x.Attribute("Include") == "Microsoft.CodeAnalysis.CSharp.Scripting"
            );

        Assert.Equal("4.4.0", (string?)entry.Attribute("Version"));
    }

    private static string ProjectPath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }
}
