# @hotrepl/cli

## 3.0.1

### Patch Changes

- Updated dependencies
  [[`74d2b68`](https://github.com/glockyco/HotRepl/commit/74d2b68069ceaa502f8349aee439b2b4548e90b0),
  [`5725a51`](https://github.com/glockyco/HotRepl/commit/5725a51cef3db2b46475c6d007b94b1de6b742e7)]:
  - @hotrepl/sdk@4.0.0

## 3.0.0

### Major Changes

- [`f6a4dcf`](https://github.com/glockyco/HotRepl/commit/f6a4dcf0e3a752914845f99b5aa00edbd7712d4b)
  Thanks [@glockyco](https://github.com/glockyco)! - Make `hotrepl` behave like a well-mannered Unix
  CLI.

  - Exit codes now follow `sysexits.h` (`64` `EX_USAGE`, `69` `EX_UNAVAILABLE`, `70` `EX_SOFTWARE`,
    `73` `EX_CANTCREAT`) instead of arbitrary `1`. CI scripts that branched on `exit 1` from
    unreachable backends should branch on `69` instead.
  - `SIGINT` exits with `130` (`128 + signal`) regardless of where the CLI is in its lifecycle.
  - The session opened by the CLI is now closed before the process returns, so `node hotrepl info`
    exits cleanly instead of hanging on the open WebSocket.
  - Errors print on stderr; stdout stays parseable JSON when `--json` is requested.
  - The `hotrepl` bin now resolves to the compiled `dist/bin.js` instead of the source
    `src/index.ts`, matching the `dist/` build emitted by `tsup`.

  Migration:

  ```bash
  # before — match any non-zero
  if ! hotrepl info; then ...

  # after — match specifically "backend unreachable"
  if ! hotrepl info; then
    case "$?" in
      69) echo "game not running";;
      *)  echo "other failure";;
    esac
  fi
  ```

### Patch Changes

- Updated dependencies
  [[`f6a4dcf`](https://github.com/glockyco/HotRepl/commit/f6a4dcf0e3a752914845f99b5aa00edbd7712d4b)]:
  - @hotrepl/sdk@3.0.0
