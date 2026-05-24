# HotRepl.Testing

In-process test helpers for HotRepl typed-command authors and SDK consumers. Pairs with
[HotRepl.Sdk](https://github.com/glockyco/HotRepl/tree/main/src/HotRepl.Sdk) for the runtime client
and [HotRepl.Core](https://github.com/glockyco/HotRepl/tree/main/src/HotRepl.Core) for the authoring
surface.

## Requirements

- .NET Standard 2.0 consumer (targets `netstandard2.0`).
- xUnit (or any test framework); the helpers are framework-agnostic.

## Quickstart

```csharp
using HotRepl.Testing;

[Fact]
public async Task Example_Command_Returns_Expected_Output()
{
    var result = await HandlerHarness.RunAsync(
        new ExampleCommand(),
        new ExampleArgs { Name = "Ada" });

    Assert.True(result.Succeeded);
    Assert.Equal("hello Ada", result.Output.Reply);
}

[Fact]
public void Example_Args_Reject_Missing_Name()
{
    var validation = HandlerHarness.Validate<ExampleArgs>("{}");
    Assert.False(validation.Ok);
}
```

## Reference

- Repository: [github.com/glockyco/HotRepl](https://github.com/glockyco/HotRepl).
- Command authoring guide:
  [`docs/authoring-commands.md`](https://github.com/glockyco/HotRepl/blob/main/docs/authoring-commands.md).
- Runtime client: [`HotRepl.Sdk`](https://github.com/glockyco/HotRepl/tree/main/src/HotRepl.Sdk).

## License

MIT
