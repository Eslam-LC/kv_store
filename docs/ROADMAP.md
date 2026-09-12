# Roadmap

## M1 — Crash-recoverable KV store (done)

In-memory dictionary + WAL + snapshot + CLI. Every write durable before
acknowledged; full crash recovery via replay.

## M2 — LSM-tree (done)

- Skip list memtable (done)
- SSTable flush + bloom filter + sparse index (done)
- Range queries (done)

Compaction shipped in M3.

WAL still protects the memtable; SSTables replace Snapshot as the durable
checkpoint mechanism.

## M3 — Compaction & multi-SSTable read path (done)

- **Compaction**: produced `CompactFlat` — a full flatten of the catalog into
  one table, triggered automatically when the table count exceeds a threshold
  (see docs/DESIGN.md "Compaction"). Tombstones are pruned because nothing
  survives below a full flatten.
- **Tombstone handling**: solved by the flatten-above invariant — tombstones
  live only in a context where the merge can prove no older source exists.
- **Read path across N SSTables**: memtable → frozen stores → SSTables tested
  newest-first with bloom-first short-circuiting; live and correct.
- **File naming/versioning + startup discovery**: `SSTable-<5 digits>`
  catalogued newest-first on `Init`; compaction keeps serials dense-bottomed.

## M4 — Concurrency

Single-threaded/single-process today. Real systems support concurrent reads
during writes, and concurrent writers.

- **Performance baseline harness (pulled ahead of M7).** A stopwatch-based
  benchmark (`dotnet run -- --benchmark`) measuring put/delete/get/scan/flush —
  captured as a `before.txt` now, rerun and diffed after every concurrency
  change. Numbers gate the work; the single-threaded "before" is only
  capturable before the engine gets restructured.
- Reader-writer locking or lock-free structures for the memtable (skip lists
  are a common choice specifically because they support lock-free/fine-grained
  concurrent access better than balanced trees).
- Background compaction on its own thread/task without blocking reads/writes.

This is where a "genuinely complicated system" starts showing real teeth for a
portfolio review — concurrency bugs are the hardest class yet.

## M5 — Transactions / atomic multi-key operations

- Batched writes (multiple PUT/DELETE atomically applied as one WAL entry,
  all-or-nothing).
- Optional: simple MVCC (snapshot isolation) — reads see a consistent
  point-in-time view even while writes are in-flight. Big scope jump; could be
  simplified to just "atomic batches" without full isolation semantics.

## M6 — Network server

- Wrap the engine behind a TCP protocol (simple length-prefixed
  request/response, reusing existing binary framing instincts) so it's a real
  client-server store, not just a CLI. Was flagged as a "stretch" goal back in
  the original project scope.
- Turns this from "a library" into "a database" — explicitly part of the ask
  for a portfolio-differentiating system.

## M7 — Benchmarking & write-up

- **Formalize the M4 harness into the final suite:** percentile latencies
  (p99), read/write mixes, multi-threaded scaling (now measurable), and the
  optional SQLite / M1 dict-based comparison target the harness primitives
  already support. The before/after numbers M4 produced carry over directly.
- Final design doc update covering the full architecture — the artifact a
  reviewer actually reads.

## M8 — Bonus: Generic engine

Make the engine and its storage components generic/reusable (Generic
Reader/Writer/Engine). Deferred from M2; moved here to keep the core LSM
implementation focused while preserving the goal for future work.