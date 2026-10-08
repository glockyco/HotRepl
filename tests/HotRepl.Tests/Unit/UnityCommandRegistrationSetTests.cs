using System;
using System.Threading;
using System.Threading.Tasks;
using HotRepl.Control;
using HotRepl.UnityCommands;
using Xunit;

namespace HotRepl.Tests.Unit;

public sealed class UnityCommandRegistrationSetTests
{
    [Fact]
    public void Dispose_RemovesCommandsAndAllowsReload()
    {
        var registry = new GlobalControlCommandRegistry();
        var names = new[] { "test.first", "test.second" };
        var registrations = UnityCommandRegistrationSet.Register(
            names,
            i => registry.Register(new Handler(names[i])),
            true,
            "",
            _ => { }
        );
        Assert.Equal(2, registry.Describe().Count);
        registrations.Dispose();
        registrations.Dispose();
        Assert.Empty(registry.Describe());
        using var reloaded = UnityCommandRegistrationSet.Register(
            names,
            i => registry.Register(new Handler(names[i])),
            true,
            "",
            _ => { }
        );
        Assert.Equal(2, registry.Describe().Count);
    }

    [Fact]
    public void Register_FiltersTrimmedDisabledNames()
    {
        var registry = new GlobalControlCommandRegistry();
        var names = new[] { "test.first", "test.second" };
        using var registrations = UnityCommandRegistrationSet.Register(
            names,
            i => registry.Register(new Handler(names[i])),
            true,
            " test.second, , test.second ",
            _ => { }
        );
        Assert.Equal("test.first", Assert.Single(registry.Describe()).Name);
    }

    [Fact]
    public void Register_MasterSwitchRegistersNothing()
    {
        using var registrations = UnityCommandRegistrationSet.Register(
            new[] { "test.first" },
            _ => throw new InvalidOperationException("Must not register disabled catalog."),
            false,
            "",
            _ => { }
        );
    }

    [Fact]
    public void Register_FailureRemovesEarlierRegistrations()
    {
        var registry = new GlobalControlCommandRegistry();
        var names = new[] { "test.first", "test.first" };
        Assert.Throws<InvalidOperationException>(() =>
            UnityCommandRegistrationSet.Register(
                names,
                i => registry.Register(new Handler(names[i])),
                true,
                "",
                _ => { }
            )
        );
        Assert.Empty(registry.Describe());
    }

    private sealed class Handler : IControlCommandHandler<EmptyArgs, EmptyArgs>
    {
        public Handler(string name) => Name = name;

        public string Name { get; }
        public int Version => 1;
        public ControlCommandKind Kind => ControlCommandKind.Sync;
        public bool MutatesState => false;

        public ValueTask<ControlCommandResult<EmptyArgs>> ExecuteAsync(
            ControlCommandContext<EmptyArgs> context,
            EmptyArgs args,
            CancellationToken cancellationToken
        ) => new(ControlCommandResult.Ok(new EmptyArgs()));
    }
}
