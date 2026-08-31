# KV Store

A crash-recoverable, in-memory key-value store with a Write-Ahead Log (WAL)
and snapshotting, exposed through a REPL command-line interface.

Keys are UTF-8 strings. Values are arbitrary byte arrays. A write-ahead log
durably records every mutation before it is applied to memory; on startup the
store replays the log to recover state. Snapshots compress the log so it does
not grow without bound.

## Features

- **Durability** — every `put`/`delete` is flushed to the WAL before it is acknowledged.
- **Crash recovery** — on startup, load snapshot (if any), then replay the WAL.
- **Integrity** — each WAL record carries a CRC32 checksum; truncated or corrupted
  records are detected and reported.
- **Snapshotting** — `snapshot save` writes the full dataset and truncates the WAL.
- **Binary values** — store and retrieve raw bytes in hex via `puthex`/`gethex`,
  or a `0x` prefix in `put`.
- **Text values** — view stored bytes as UTF-8 via `get`.
- **Configurable storage** — point the WAL and snapshot anywhere with `--data-dir`; the directory is auto-created.
- **Minimal footprint** — only System.CommandLine and System.IO.Hashing; no database engine.

## Requirements

- .NET 10 SDK

## Build & Run

```bash
dotnet build
dotnet run
```

Run from the project root so the default `./data` paths resolve. When debugging,
point your debugger's working directory at the project root.

### Configurable data directory

By default the store keeps its WAL and snapshot in `./data`. Point it elsewhere
once at startup with `--data-dir` (or `-d`); the directory is created if missing:

```bash
dotnet run -- --data-dir /tmp/kv
```

The option applies to the whole session — subsequent REPL commands
(`put`, `get`, `snapshot save`, ...) use that directory without re-specifying it.

## Usage

Interactive REPL. Commands:

| Command                 | Description                                                                                                       |
| ----------------------- | ----------------------------------------------------------------------------------------------------------------- |
| `put <key> <value...>`  | Insert/overwrite a key. Tokens are concatenated; quote to keep spaces, or prefix a token with `0x` for raw bytes. |
| `puthex <key> <hex...>` | Insert a key whose value is given as hex (e.g. `0xDEADBEEF`).                                                     |
| `get <key>`             | Print the value as a UTF-8 string.                                                                                |
| `gethex <key>`          | Print the value as space-separated hex bytes.                                                                     |
| `delete <key>`          | Remove a key.                                                                                                     |
| `snapshot save [path]`  | Save the full dataset, then truncate the WAL.                                                                     |
| `snapshot load [path]`  | Load a snapshot into the store.                                                                                   |
| `replay`                | Append WAL records to the store.                                                                                  |
| `exit`                  | Leave the program.                                                                                                |

> `put` and `puthex` treat each argument as one token and concatenate them. Wrap a
> value in quotes to keep spaces (`put name "hello world"`), or prefix with `0x` to
> write literal bytes. `put` value that is neither quoted nor `0x`-prefixed is
> UTF-8 encoded.

### Example

```
$ dotnet run
> WAL appended from: ./data/wal_log.
> put name "hello world"
key: name was inserted.
> puthex flag DEADBEEF
key: flag was inserted.
> get name
name : hello world
> gethex flag
flag : DE AD BE EF
> snapshot save
snapshot saved.
> replay
WAL restored.
> delete name
key: name was deleted.
> exit
Thank you for using the application.
```

### Crash Recovery

Startup loads `<data-dir>/snapshot.dat` (if present), then replays
`<data-dir>/wal_log` (if present) on top. Because the log is truncated after a
snapshot is saved, the snapshot plus subsequent log entries reconstruct the
full state. With the default data directory these are `./data/snapshot.dat` and
`./data/wal_log`.

Recovery is **not** automatic on interactive `snapshot load` — use `replay`
explicitly to append WAL records after loading a snapshot. `replay` is also
available to re-apply the log on demand.

## Notes & Limitations

- Single-process; no threading or concurrent access.
- `get` decodes bytes as UTF-8; non-text data should be read with `gethex`.
- A corrupt WAL halts recovery at the first bad record (no partial recovery).
- No authentication, networking, or persistence beyond the WAL/snapshot files.

## Documentation

- [Design Document](docs/DESIGN.md) — architecture, on-disk formats, and design trade-offs.
