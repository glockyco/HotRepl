using HotRepl.Evaluator;
using Xunit;

namespace HotRepl.Tests.Unit;

public class AssemblyFilterTests
{
    [Theory]
    [InlineData("mscorlib")]
    [InlineData("System")]
    [InlineData("System.Core")]
    [InlineData("System.Xml")]
    [InlineData("completions")]
    [InlineData("netstandard")]
    public void IsFiltered_StdlibAndArtifacts(string name)
    {
        Assert.True(AssemblyFilter.IsFiltered(name));
    }

    [Theory]
    [InlineData("MSCORLIB")]
    [InlineData("system")]
    [InlineData("System.CORE")]
    public void IsFiltered_CaseInsensitive(string name)
    {
        Assert.True(AssemblyFilter.IsFiltered(name));
    }

    [Theory]
    [InlineData("UnityEngine")]
    [InlineData("Newtonsoft.Json")]
    [InlineData("HotRepl.Core")]
    public void IsFiltered_ReturnsFalse_ForNonStdLib(string name)
    {
        Assert.False(AssemblyFilter.IsFiltered(name));
    }
}
