using HotRepl.Evaluation;
using Xunit;

namespace HotRepl.Tests.Unit;

public class AssemblyFilterTests
{
    [Theory]
    [InlineData("mscorlib")]
    [InlineData("System")]
    [InlineData("System.Core")]
    [InlineData("System.Xml")]
    public void Contains_AllStdLibNames(string name)
    {
        Assert.Contains(name, AssemblyFilter.StdLibNames);
    }

    [Theory]
    [InlineData("MSCORLIB")]
    [InlineData("system")]
    [InlineData("System.CORE")]
    public void CaseInsensitive(string name)
    {
        Assert.Contains(name, AssemblyFilter.StdLibNames);
    }

    [Theory]
    [InlineData("UnityEngine")]
    [InlineData("Newtonsoft.Json")]
    [InlineData("HotRepl.Core")]
    public void DoesNotContain_NonStdLib(string name)
    {
        Assert.DoesNotContain(name, AssemblyFilter.StdLibNames);
    }

    [Fact]
    public void HasExpectedCount()
    {
        Assert.Equal(4, AssemblyFilter.StdLibNames.Count);
    }
}
