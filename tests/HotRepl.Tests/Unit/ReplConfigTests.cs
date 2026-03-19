using HotRepl.Hosting;
using Xunit;

namespace HotRepl.Tests.Unit;

public class ReplConfigTests
{
    [Fact]
    public void Defaults_MatchSpec()
    {
        var config = new ReplConfig();

        Assert.Equal(18590, config.Port);
        Assert.Equal(10_000, config.DefaultTimeoutMs);
        Assert.Equal(100_000, config.MaxResultLength);
        Assert.Equal(100, config.MaxEnumerableElements);
    }

    [Fact]
    public void Properties_CanBeOverridden()
    {
        var config = new ReplConfig
        {
            Port = 9999,
            DefaultTimeoutMs = 5000,
            MaxResultLength = 200,
            MaxEnumerableElements = 10
        };

        Assert.Equal(9999, config.Port);
        Assert.Equal(5000, config.DefaultTimeoutMs);
        Assert.Equal(200, config.MaxResultLength);
        Assert.Equal(10, config.MaxEnumerableElements);
    }
}
