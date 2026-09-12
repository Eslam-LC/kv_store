# KV Store

A crash-recoverable key-value store: an in-memory store backed by a Write-Ahead
Log (WAL), snapshots, and immutable on-disk tables (SSTables), exposed through a
REPL command-line interface.

Keys are UTF-8 strings. Values are arbitrary byte arrays. A write-ahead log
durably records every mutation before it is applied to memory; on startup the
store replays the log to recover state. Snapshots compress the log so it does
not grow without bound. When the in-memory store crosses a size threshold it is
frozen and flushed to a sorted, immutable SSTable, keeping the WAL append-only.

## Features

- **Durability** — every `put`/`delete` is flushed to the WAL before it is acknowledged.
- **Crash recovery** — on startup, catalog existing SSTables (newest first),
  then load the snapshot (if any) and replay the WAL on top.
- **Integrity** — each WAL record and stored pair carries a CRC32 checksum;
  truncated or corrupted records are detected and reported.
- **Snapshotting** — `snapshot save` writes the full dataset and truncates the WAL.
- **SSTable flushing** — writes are accumulated in memory; crossing a size
  threshold freezes the store and flushes it to a sorted, immutable table
  searchable via a bloom filter and sparse index.
- **Range scans** — `scan <start-key> <end-key>` returns every live entry in an
  inclusive range, merged newest-first across the memtable, frozen stores, and
  SSTables; tombstones shadow older data and never resurrect.
- **Corruption isolation** — a corrupted/unsupported SSTable is quarantined
  (renamed `*.corrupt`) and reported; the rest of the store still loads.
- **Binary values** — `-x`/`--hex` on `put`, `get`, and `scan` reads and prints
  raw bytes as hex; `put -x` tokens are plain hex (no `0x` prefix).
- **Text values** — view stored bytes as UTF-8 via `get` (the default).
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
| `put <key> <value...>`  | Insert/overwrite a key. Tokens are concatenated; quote to keep spaces; `-x` reads each token as plain hex.         |
| `get <key>`             | Print the value as a UTF-8 string; `-x` prints hex in 4-char groups.                                            |
| `delete <key>`          | Remove a key.                                                                                                     |
| `scan <start> <end>`    | Print every live entry in the inclusive `[start, end]` range, merged newest-first. `-x` shows values as hex.     |
| `snapshot save [path]`  | Save the full dataset, then truncate the WAL.                                                                     |
| `snapshot load [path]`  | Load a snapshot into the store.                                                                                   |
| `replay`                | Append WAL records to the store.                                                                                  |
| `exit`                  | Leave the program.                                                                                                |

> `-x`/`--hex` dictates the format of **all** tokens on `put`, `get`, and `scan` —
> hex in, hex out. `put` treats each argument as one token and concatenates them;
> wrap a value in quotes to keep spaces (`put name "hello world"`). With `put -x`,
> each value token is parsed as plain hex (`put -x flag DEADBEEF`); without `-x`,
> values are UTF-8 encoded. `get -x` and `scan -x` print each value as hex in
> 4-char groups (e.g. `DEAD BEEF`).

### Example

```
$ dotnet run
> WAL appended from: ./data/wal_log.
> put name "hello world"
key: name was inserted.
> put -x flag DEADBEEF
key: flag was inserted.
> get name
hello world
Retrieved 'name' (11 chars).
> get -x flag
DEAD BEEF
Retrieved 'flag' (9 chars).
> put a foo
key: a was inserted.
> put b bar
key: b was inserted.
> scan a c
Key                 Value                                              
----------------------------------------------------------------------
a                   foo                                               
b                   bar                                               
Scanned 'a'..'c': 2 pair(s).
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

Startup catalogs every `SSTable-<5-digits>` file (newest first), then loads
`<data-dir>/snapshot.dat` (if present) and replays `<data-dir>/wal_log` (if
present) on top. The in-memory store, replayed WAL, and immutable tables
together reconstruct the full state. With the default data directory these are
`./data/snapshot.dat`, `./data/wal_log`, and `./data/SSTable-*`.

Recovery is **not** automatic on interactive `snapshot load` — use `replay`
explicitly to append WAL records after loading a snapshot. `replay` is also
available to re-apply the log on demand.

## Notes & Limitations

- Single-process; no threading or concurrent access.
- `get` decodes bytes as UTF-8; non-text data should be read with `get -x`.
- A corrupt WAL halts recovery at the first bad record (no partial recovery).
- A corrupted/unsupported SSTable aborts `Init` only if it is not a quarantine-able
  error; quarantine-able tables are moved to `*.corrupt` and skipped.
- `delete` writes a tombstone (a null value). Reads of a deleted key are
  reported as not-found and never fall through to older data; the key bytes stay
  in memory and on disk until a future compaction pass reclaims them.
- No authentication, networking, or persistence beyond the WAL/snapshot/SSTable files.

## Documentation

- [Design Document](docs/DESIGN.md) — architecture, on-disk formats, and design trade-offs.
- [Roadmap](docs/ROADMAP.md) — milestone plan and current progress.
