# Roadmap

## M1 — Crash-recoverable KV store (done)

In-memory dictionary + WAL + snapshot + CLI. Every write durable before
acknowledged; full crash recovery via replay.

## M2 — LSM-tree (in progress)

- Skip list memtable (done)
- Generic Reader/Writer/Engine (deferred)
- SSTable flush + bloom filter + sparse index (in progress)
- Compaction
- Range queries

WAL still protects the memtable; SSTables replace Snapshot as the durable
checkpoint mechanism.

## M3 — Compaction & multi-SSTable read path

- **Leveled or size-tiered compaction**: merge multiple SSTables into fewer,
  larger ones; drop entries shadowed by newer writes or past their DELETE
  tombstone.
- **Tombstone handling**: a DELETE in a newer SSTable must correctly shadow a
  PUT in an older one during merge and during reads. Real correctness trap — a
  naive merge that just "keeps the newest value per key" must also eventually
  drop tombstones once no older SSTable holds the shadowed key, or they
  accumulate forever.
- **Read path across N SSTables**: active memtable → immutable/flushing
  memtables → SSTables newest-to-oldest, short-circuiting on first hit (bloom
  filter first, to skip most files).
- **File naming/versioning + startup discovery**: scan the data directory,
  order SSTables by generation/timestamp, so a restart correctly rebuilds the
  read order.

## M4 — Concurrency

Single-threaded/single-process today. Real systems support concurrent reads
during writes, and concurrent writers.

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

- Formal benchmark suite (throughput, p99 latency, read/write ratios) compared
  against a baseline (SQLite, or the M1 dict-based version) to quantify what
  the LSM/SSTable work actually bought.
- Final design doc update covering the full architecture — the artifact a
  reviewer actually reads.