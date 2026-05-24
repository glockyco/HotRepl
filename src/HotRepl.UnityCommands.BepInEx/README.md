# HotRepl.UnityCommands.BepInEx

Canonical sample plugin demonstrating HotRepl typed-command authoring for Unity games running
BepInEx + Mono.

This project packages the shared command catalog from `src/HotRepl.UnityCommands/Commands/` into a
BepInEx-compatible assembly. New BepInEx command plugins should copy the catalog structure from the
shared project instead of duplicating loader-specific command logic.

See [`docs/authoring-commands.md`](../../docs/authoring-commands.md) for the typed-command authoring
walkthrough.
