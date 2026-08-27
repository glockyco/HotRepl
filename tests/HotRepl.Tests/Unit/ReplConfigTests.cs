using System;
using HotRepl;
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
        Assert.Null(config.DefaultEvaluatorName);
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
            MaxEnumerableElements = 10,
            DefaultEvaluatorName = "Roslyn.Script",
        };

        Assert.Equal(9999, config.Port);
        Assert.Equal(5000, config.DefaultTimeoutMs);
        Assert.Equal(200, config.MaxResultLength);
        Assert.Equal(10, config.MaxEnumerableElements);
        Assert.Equal("Roslyn.Script", config.DefaultEvaluatorName);
    }

    [Theory]
    [InlineData("18591", 18591)]
    [InlineData(" 18591 ", 18591)]
    [InlineData("1", 1)]
    [InlineData("65535", 65535)]
    public void TryParsePort_accepts_a_port(string value, int expected)
    {
        Assert.True(ReplConfig.TryParsePort(value, out var port));
        Assert.Equal(expected, port);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("18591.5")]
    [InlineData("0x4897")]
    [InlineData("port")]
    public void TryParsePort_rejects_anything_else(string? value)
    {
        Assert.False(ReplConfig.TryParsePort(value, out var port));
        Assert.Equal(0, port);
    }

    [Fact]
    public void ApplyEnvironmentOverrides_moves_the_port()
    {
        using var variable = new EnvironmentVariable(ReplConfig.PortVariable, "18591");

        var config = new ReplConfig().ApplyEnvironmentOverrides();

        Assert.Equal(18591, config.Port);
    }

    [Fact]
    public void ApplyEnvironmentOverrides_keeps_the_default_for_unusable_text()
    {
        using var variable = new EnvironmentVariable(ReplConfig.PortVariable, "not-a-port");

        var config = new ReplConfig().ApplyEnvironmentOverrides();

        Assert.Equal(18590, config.Port);
    }

    [Fact]
    public void ApplyEnvironmentOverrides_keeps_the_default_when_absent()
    {
        using var variable = new EnvironmentVariable(ReplConfig.PortVariable, null);

        var config = new ReplConfig().ApplyEnvironmentOverrides();

        Assert.Equal(18590, config.Port);
    }

    private sealed class EnvironmentVariable : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvironmentVariable(string name, string? value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}
