using System;
using HotRepl;
using Xunit;

namespace HotRepl.Tests.Unit;

public sealed class ReplConfigExposurePolicyTests
{
    [Fact]
    public void Validate_AllowsDefaultLoopback()
    {
        var config = new ReplConfig();

        var result = ReplConfigExposurePolicy.Validate(config);

        Assert.True(result.IsSafeDefault);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Validate_WarnsWhenNonLoopbackBindHasNoLoopbackAuthority()
    {
        var config = new ReplConfig { BindHost = "0.0.0.0" };

        var result = ReplConfigExposurePolicy.Validate(config);

        Assert.False(result.IsSafeDefault);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("0.0.0.0", warning, StringComparison.Ordinal);
        Assert.Contains("loopback", warning, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ControlAuthToken", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplConfigExposurePolicy_DoesNotExposeAuthorityMutation()
    {
        Assert.Null(typeof(ReplConfigExposurePolicy).GetMethod("ApplyControlAuthToken"));
    }
}
