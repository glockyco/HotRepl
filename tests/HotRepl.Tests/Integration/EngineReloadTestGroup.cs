using Xunit;

namespace HotRepl.Tests.Integration;

// Console.Out and FleckLog are process-wide; evaluator tests can replace stdout concurrently.
[CollectionDefinition("Engine reload", DisableParallelization = true)]
public sealed class EngineReloadTestGroup;
