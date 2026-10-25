# HotRepl

HotRepl lets coding agents and mod developers inspect a running Unity game, evaluate C# and call
typed commands without restarting it. It runs inside the game through BepInEx or Lunaris on Mono, or
MelonLoader on IL2CPP, with a local WebSocket connection for the CLI, MCP server and SDKs.

Use raw C# evaluation to explore a game and typed commands for repeatable automation, data exports
and live-game tests.

[Website](https://hotrepl.glockyco.com) · [npm packages](https://www.npmjs.com/org/hotrepl) ·
[Mod template](https://github.com/glockyco/hotrepl-mod-template)

## Set up a game

You need a Unity game with the matching mod loader installed. The npm packages are clients, not game
plugins. Install a HotRepl host in the game before connecting a client.

Clone the repository, then follow the setup for your loader. The examples use the pinned Nix
environment on macOS or Linux and read assemblies from your game installation.

```sh
git clone https://github.com/glockyco/HotRepl.git
cd HotRepl
```

### BepInEx or MelonLoader

For BepInEx on Mono, replace the assembly path with your game's managed assembly directory:

```sh
nix run .#build-loader -- \
  --loader bepinex \
  --assemblies /path/to/Game/Game_Data/Managed \
  --output ./hotrepl-host
```

For MelonLoader on IL2CPP, provide the Unity dependency assemblies, loader directory and generated
IL2CPP assemblies:

```sh
nix run .#build-loader -- \
  --loader melonloader \
  --assemblies /path/to/UnityDependencies \
  --melonloader /path/to/Game/MelonLoader \
  --il2cpp /path/to/Game/MelonLoader/Il2CppAssemblies \
  --output ./hotrepl-host
```

The output directory must not already exist. The builder writes the host, its dependencies and a
build manifest there. It does not change the game installation.

Copy the output DLLs together into a HotRepl subfolder of the game's BepInEx plugins directory, or
into the game's Mods directory for MelonLoader. For direct .NET builds and local assembly
configuration, see [AGENTS.md](AGENTS.md) and [Local.props.example](Local.props.example).

### Lunaris

Lunaris uses a separate .NET build, not the loader builder above. Enter the development shell,
restore the formatter tool and copy the game's Mono Unity assemblies into the local reference
directory. Replace the example paths with paths from your game and loader installation:

```sh
nix develop
dotnet tool restore
mkdir -p src/HotRepl.BepInEx/lib
cp /path/to/Game/Game_Data/Managed/UnityEngine.dll \
  /path/to/Game/Game_Data/Managed/UnityEngine.CoreModule.dll \
  src/HotRepl.BepInEx/lib/
dotnet build src/HotRepl.Host.Lunaris/HotRepl.Host.Lunaris.csproj --nologo -v q \
  -p:LunarisPath="/path/to/Lunaris.dll"
```

Copy only the contents of `src/HotRepl.Host.Lunaris/bin/Debug/netstandard2.1/plugins/HotRepl/` into
a HotRepl subfolder of the game's plugins directory. Do not copy DLLs from another host's output
because Lunaris loads dependency DLLs as shared libraries and supplies its own loader dependencies.

Start the game and enable HotRepl in Lunaris's in-game plugin installer. Manually installed plugins
start disabled. The Lunaris host includes the Unity command catalog, so no separate commands plugin
is needed.

### Choose process-specific settings

Start the game before connecting a client. If you run several game processes, give each its own
`HOTREPL_PORT` and `HOTREPL_ARTIFACT_DIR` environment values. These override saved host
configuration for that process. Point the client at the corresponding endpoint with `HOTREPL_URL`
for the CLI and MCP server, or the SDK's explicit connection options.

## Connect from the command line

With Node.js and npm installed, check that the game is reachable and evaluate a live value:

```sh
npx -y @hotrepl/cli info
npx -y @hotrepl/cli eval 'UnityEngine.Application.productName'
```

Inspect a built-in command's arguments, then run it:

```sh
npx -y @hotrepl/cli describe unity.app.info --json
npx -y @hotrepl/cli run unity.app.info '{}'
```

Set `HOTREPL_URL` for the CLI or MCP server when connecting to a different endpoint. See the
[CLI README](packages/cli/README.md) for watches, journal queries, artifact reads and output
formats.

If the connection fails, check the game's loader log for HotRepl startup errors. A new client
connection replaces the existing session, so close other CLI or MCP sessions before connecting.

## Connect a coding agent

Add HotRepl to your MCP host's server configuration. For hosts that use an `mcpServers` object:

```json
{
  "mcpServers": {
    "hotrepl": {
      "command": "npx",
      "args": ["-y", "@hotrepl/mcp"]
    }
  }
}
```

Follow your host's reload procedure, then ask the agent to inspect the runtime or list its commands.
The [MCP README](packages/mcp/README.md) describes the tools and connection settings.

## Automate from TypeScript or C#

Install the TypeScript SDK in your client project:

```sh
npm install @hotrepl/sdk
```

```ts
import { connect } from "@hotrepl/sdk";

const session = await connect();
try {
  console.log(await session.eval<string>("UnityEngine.Application.productName"));
  console.log(await session.run("unity.app.info", {}));
} finally {
  session.close();
}
```

The [SDK README](packages/sdk/README.md) covers connection options and session methods. C# clients
and live-game tests use [HotRepl.Sdk](src/HotRepl.Sdk) and [HotRepl.Testing](src/HotRepl.Testing).
Their local packing commands are in [AGENTS.md](AGENTS.md).

To add your own game commands, start with the
[mod template](https://github.com/glockyco/hotrepl-mod-template) and the
[command authoring guide](docs/authoring-commands.md).

## Runtime access and files

HotRepl executes submitted C# on the game's main thread. Evaluation and commands can change game
state or write files with the game's permissions. Keep the server on loopback because the protocol
has no authentication.

Commands can write persistent artifact files. [ReplConfig](src/HotRepl.Core/ReplConfig.cs) defines
the bind address, port and artifact directory settings. The
[artifact reference](docs/control-plane-protocol.md#artifacts) explains file lifetime and path
mapping for games under Wine or Proton.

Evaluation state persists until reset. Reset does not unload every generated type, and an infinite
loop can hang a Mono game despite a timeout. See
[AGENTS.md](AGENTS.md#domain-constraints-agents-often-get-wrong) for evaluator limits before running
untrusted or long-running snippets.

## Reference and development

- [Command authoring](docs/authoring-commands.md) covers schemas, handlers, jobs and artifacts.
- [Protocol reference](docs/control-plane-protocol.md) and
  [protocol package](packages/protocol/README.md) cover custom clients.
- [Behavior specifications](openspec/specs) describe the runtime contracts.
- [AGENTS.md](AGENTS.md) covers development setup, targeted checks, host builds and architecture
  constraints.
- [Site package](site/package.json) defines website development and build commands.
- [Changesets guide](.changeset/README.md) explains package release contributions.

For development, enter the pinned Nix shell and restore dependencies:

```sh
nix develop
dotnet tool restore
bun install --frozen-lockfile
lefthook install
```

Run the repository's CI gate from the repository root:

```sh
nix run .#check
```

## License

[MIT](LICENSE).
