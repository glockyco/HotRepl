using Xunit;

namespace HotRepl.Tests.Integration;

// Console.Out and FleckLog are process-wide, and a started engine installs a console capture.
[CollectionDefinition("Engine reload", DisableParallelization = true)]
public sealed class EngineReloadTestGroup;
