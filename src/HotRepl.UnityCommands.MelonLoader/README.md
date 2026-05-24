# HotRepl.UnityCommands.MelonLoader

Canonical sample mod demonstrating HotRepl typed-command authoring for Unity games running
MelonLoader + IL2CPP.

This project packages the shared command catalog from `src/HotRepl.UnityCommands/Commands/` into a
MelonLoader-compatible assembly. New MelonLoader command mods should copy the catalog structure from
the shared project instead of duplicating loader-specific command logic.

See [`docs/authoring-commands.md`](../../docs/authoring-commands.md) for the typed-command authoring
walkthrough.
