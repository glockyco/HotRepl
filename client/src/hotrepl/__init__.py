"""Reference Python client for the HotRepl C# REPL protocol."""

from hotrepl._client import Client, EvalError, ServerUnreachableError

__all__ = ["Client", "EvalError", "ServerUnreachableError"]
