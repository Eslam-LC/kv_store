# KV Store

LSM-style key/value store. C# / .NET 10 console app (System.CommandLine REPL) with storage logic in `Implementations/`. See `README.md` (usage), `docs/DESIGN.md` (architecture) — keep both in sync when on-disk formats change — and `docs/ROADMAP.md` (milestone plan). Current milestone: **M2 LSM-tree in progress** (SSTable flush + bloom + sparse index; compaction and range queries next).

## Commands

- Build: `dotnet build` (root project `kv-store.csproj`, net10.0)
- Test all: `dotnet test tests/KvStore.Tests/KvStore.Tests.csproj`
- One test: `dotnet test tests/KvStore.Tests/KvStore.Tests.csproj --filter "FullyQualifiedName~WAEngineTests.Flush_TwoTables"`
- Run REPL: `dotnet run` from repo root (default `./data` dir), or `dotnet run -- --data-dir /tmp/kv`
- REPL commands: `put`, `get`, `delete`, `snapshot save|load`, `replay`, `exit`. Values are UTF-8 text; hex via `puthex`/`gethex` or `0x` prefix.

## Workflow rules (project convention)

- **Remind the user to commit after each completed task/prompt.** The user wants to be prompted — do not end a task while changes are sitting uncommitted. Small diffs are what make debugging tractable here. If a large uncommitted diff accumulates (many files in `git status`), pause and ask the user to commit before continuing.
- **When reminding about a commit, suggest a concrete commit message** (short, imperative, matches existing history style) so the user can commit without hunting for a message.
- **Update `AGENTS.md` after major changes.** When a task significantly changes architecture, commands, on-disk formats, or workflow (not cosmetic edits), remind the user to update `AGENTS.md` (and `README.md`/`docs/DESIGN.md` if affected) so future sessions get accurate project state.
- **Diff-first debugging.** Before diagnosing, run `git log --oneline -5` and `git diff` on the file in question — most bugs here were introduced by the last uncommitted refactor, not ancient code.
- **Split-brain editing (critical):** assistant edits tests (`tests/KvStore.Tests/`) directly. Product code (`Program.cs`, `Implementations/`, `Enums/`) is only ever *proposed* — the user applies those edits.
- Any transient product-code instrumentation must be announced (file:line), run, then reverted before the task ends.

## Architecture notes

- **Tombstone = `null` value.** `KeyValueStore.TryGet` returns `KeyWasDeleted`; `WAEngine.TryGet` normalizes that to `KeyWasNotFound` AND stops searching older stores (tombstone is terminal, no resurrection).
- **Memory accounting is byte-delta based** (`KeyValueStore.Put`/`Delete`): an absent-key delete still adds the key's bytes (tombstone node keeps them in the skip list). Pin tests: `KeyValueStoreTests.Delete_LiveKey_CountsKeyBytesOnce_ForTombstone`, `Delete_AbsentKey_CountsKeyBytes_ForTombstone`.
- **Record frame** (WAL / snapshot / SSTable): `[crc32 4B][op 1B][key][vlen 4B + value, PUT only]`. DELETE carries no value (replays as `null`).
- **SSTable file layout**: `[magic4][records][sparse index][bloom bytes][first/last key][Footer]`. Footer is fixed 52B; read by `Seek(-52, End)`. Newest table sits first in `immutableSSTables`; `TryReadEntry` lazily opens the file.
- **Serial numbering gotcha**: the generated regex `^SSTable-([0-9]{5})$` **must keep its capture group** — serial extraction reads `Groups[1]`. Without the `(...)`, `Groups[1]` is `""`, serial always resolves to 0, and every flush targets `SSTable-00001` (collision → `File.Move` throws `IOException`). One char of polish here broke 2 flush tests.
- **Error code names were renamed.** `Enums/ErrorCode.cs` now uses `InputOutputFailed`, `KeyWasNotFound`, `InstanceIsNotInitialized`, etc. (legacy `IOError`/`KeyNotFound`/`UnInitializedInstance` don't exist). `MapExToEr.GetErrorCode` maps exception types to codes.
- `WAReader` carries a dead `KeyNotFound && DELETE → continue` branch — candidate for deletion.

## Testing quirks

- Every test creates a fresh GUID temp dir under `Path.GetTempPath()` and cleans up best-effort. No shared fixtures, no ordering guarantees.
- Test project mirrors source classes one-to-one (`WAEngineTests.cs`, `SSTableTests.cs`, `BloomFilterTests.cs`, ...).
- Only dependencies: `System.CommandLine`, `System.IO.Hashing`.