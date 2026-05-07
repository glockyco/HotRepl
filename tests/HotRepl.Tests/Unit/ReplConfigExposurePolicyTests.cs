using HotRepl;
using Xunit;

namespace HotRepl.Tests.Unit;

public sealed class ReplConfigExposurePolicyTests
{
    [Fact]
    public void Validate_AllowsDefaultLoopbackWithoutAuthToken()
    {
        var config = new ReplConfig();

        var result = ReplConfigExposurePolicy.Validate(config);

        Assert.True(result.IsSafeDefault);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Validate_WarnsWhenNonLoopbackBindHasNoControlAuthToken()
    {
        var config = new ReplConfig { BindHost = "0.0.0.0" };

        var result = ReplConfigExposurePolicy.Validate(config);

        Assert.False(result.IsSafeDefault);
        Assert.Contains(result.Warnings, warning => warning.Contains("0.0.0.0") && warning.Contains("ControlAuthToken"));
    }

    [Fact]
    public void ApplyControlAuthToken_EnablesControlAuthWhenTokenIsProvided()
    {
        var config = new ReplConfig { ControlAuthToken = "local-secret" };

        ReplConfigExposurePolicy.ApplyControlAuthToken(config);

        Assert.True(config.RequireControlAuth);
    }
}
