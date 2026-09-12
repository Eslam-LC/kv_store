# Benchmark Harness — Review & Rework Spec

Stage: **pre-M4 Concurrency — performance baseline**. This document records the
critical review of `Implementations/Benchmark.cs`, the product seams the harness
depends on, and the concrete spec for the reworked `Run()` (randomized keys,
deterministic values, integrity sweep, error accounting, state-line) to be
implemented as the next step. The M4 milestone rule: capture `before.txt` now,
rerun and diff after every concurrency change.

---

## Status

- **Implemented (build-pass, this stage):** the three missing helpers
  (`TableGet`, `Scan`, `TableSizeMb`) and the phase reorder so every phase
  measures what it claims — `warmup → put → flush → table-get → scan → delete → get`.
  This still uses sequential keys and `Random.Shared` values; those are fixed in
  the rework below.
- **Wiring (yours):** `Program.cs` `--benchmark` branch — `Benchmark.Run` must be
  given an *initialized* engine (see seam (a)), plus `--ops`/`--seed` options.
- **Pending (this spec):** randomized key layout, deterministic value function,
  integrity sweep, per-phase error accounting, state-line, engine memory accessor.

---

## 1. Original review — findings

### Blockers — the harness measured nothing

1. **`--benchmark` never initializes the engine.** `Program.cs:26-30` branches to
   `Benchmark.Run` before the REPL path constructs and inits the engine. `Run`
   created `WAEngine(dataDir)` and never called `Init` → every op returned
   `InstanceIsNotInitialized` → swallowed → the run finished instantly, doing
   nothing, while printing a fantasy table.
2. **It did not build.** `TableGet`, `Scan`, `TableSizeMb` referenced, defined
   nowhere. (Fixed in the build-pass.)
3. **Every exit code was discarded.** `Put`/`Delete`/`TryGet`/`Scan`/`FlushToSSTable`
   all return `ErrorCode`; all thrown away. A dying run prints the same table as
   a healthy one. Numbers without error counts are a PR number generator.
4. **Read phases never touched disk.** Order was `put → delete → get →
   table-get → scan → flush last`. Everything still sat in the memtable when the
   read phases ran — the disk path the harness exists to measure was never
   exercised.

### Major — the numbers misrepresented reality

5. **`get` was 50% tombstone-miss.** `delete` ran before `get`, making half the
   reads misses — and misses are cheaper, biasing the headline optimistic and
   uninterpretable as hit or miss.
6. **Content unreproducible.** `Random.Shared.NextBytes` per put → no read can
   be verified against its original value. Integrity checking requires
   deterministic values (`value = f(key)`), so a re-read can byte-compare.
7. **Keys were the opposite of "a mess."** Sequential `k{i:D8}` is monotonic
   insert with maximal locality, and the warmup's keys overlapped the main put
   phase (updates mixed into the insert number).
8. **Measured loop polluted with alloc/formatting.** `new byte[100]` +
   `NextBytes` + `$"k{i:D8}"` inside the timed body counted string/alloc/GC cost
   as engine cost.

### Medium

9. **Memory reporting blocked by a product seam.** `MemStore` is private; byte
   accounting exists only on `KeyValueStore.MemoryStorage`. File sizes are
   reachable today (`WALFile`/`SnapshotFile` public, table dir derived from
   `WALFile`); memtable bytes need a seam (see (b)).
10. **Mislabeled metadata.** Flush row said `Ops = ops` for one flush;
    `TotalOps = ops` undercounted the run ~3×; the `row.Ms == 0` fallback
    clobbered sub-millisecond flushes; `reportPath` was dead at the call site;
    `rootCommand.Parse(args)` ran 3×.
11. **`BenchReport`/`BenchRow`/`BenchPrinter` public in the product namespace** —
    benchmark scaffolding leaked into the library API, untested.

---

## 2. Product seams — (a), (b), (c) in detail

### (a) `Run` owns `Init`

`Benchmark.Run` takes a `WAEngine` it constructs, so it should own the full
lifecycle: `Init(out _)` before the phases, temp-dir cleanup after. Keeping
`Init` in the REPL path only means the `--benchmark` branch depends on a side
effect that lives elsewhere and is easy to bypass — this is exactly what made
the original harness a silent no-op.

### (b) Public mem-byte accessor on `WAEngine`

**Why a seam exists at all:** `WAEngine.MemStore` is a private field
(`KeyValueStore?`, WAEngine.cs:22) that is **swapped by reference** on flush
(a fresh store), on replay (copy-then-swap), and null until `Init`. Byte
accounting already lives at the inner layer — `KeyValueStore.MemoryStorage`
is a public property over the private `memoryStorage` counter, maintained as
byte-deltas on every `Put`/`Delete` (the same counter the flush threshold is
compared against). The engine just never publishes it.

**What the accessor should be** — a single read-only property:

```csharp
public long MemoryBytes =>
    (MemStore?.MemoryStorage ?? 0) + Frozen_.Sum(s => s.MemoryStorage);
```

- `MemStore?.MemoryStorage ?? 0` — nullable until `Init`; `?? 0` keeps the
  benchmark robust pre-Init. Because the field is a reference, reading the
  property always reflects the *current* memtable, even right after flush
  (fresh empty store) or replay (swapped store) — no stale snapshot.
- `+ Frozen_.Sum(...)` — in-flight immutable stores awaiting write still hold
  payload bytes; during/after a flush that's when memory peaks, so excluding
  them would make the flush state-line a lie.

**What it does NOT count:** `ImmutableSSTable`'s in-memory index/bloom arrays,
allocator slack, JIT. The property reports **logical key/value payload bytes** —
the same number the engine's own memory accounting uses — not process RSS.
For RSS, `GC.GetTotalMemory(forceFullCollection: false)` is the fallback, but
it is noisy and irreproducible, which is exactly what a diff-able baseline must
avoid. The report labels this column `mem (payload)` so nobody mistakes it for
a memory-profiler figure.

**Why the benchmark needs it:** the per-phase state-line can then answer the
M4 question "did the restructure reduce footprint at equal throughput" —
invisible in before/after otherwise.

### (c) "Tolerating the effort" of reordering phases — the real cost

Mechanically, reordering the fluent `.Phase(...)` chain is moving lines. The
**effort is semantic**: the chain is a data-flow — each phase consumes state an
earlier phase must have produced. Reorder wrongly and phases silently measure
the wrong thing (as the original did). Three orderings are load-bearing:

1. **`flush` before `table-get`/`scan`.** Reads are newest-first across
   memtable + frozen + tables. Without a flush the memtable holds everything and
   `table-get`/`scan` measure the skip list with an empty disk path — the same
   number as `get`. Only after flush does `TryReadEntry`/bloom fire per read.
2. **`delete` after `get-hit`.** `get` claims a clean hit-rate read; running
   deletes first makes it a mixed 50% miss cost with a cheaper-miss bias. To
   measure misses at all they must be deliberate → split `get-hit` (survivors)
   and `get-miss` (deleted keys) into separate honest phases.
3. **`integrity` last.** It validates the *entire accumulated state* (writes +
   deletes + flush + any auto-compaction), so it runs after every mutation
   phase. Mid-run it would validate a prefix and silently miss corruption the
   later phases caused.

Secondary couplings (the "effort" you don't see at first):

- **Warmup key space must not overlap the put phase keys** — overlap turns a
  slice of the timed puts into overwrites, mixing insert and update cost into
  the headline.
- **Scan windows must be sized to the populated key range**, and the choice of
  whether scans run over clean data or data-with-tombstones must be made
  deliberately (post-delete scans count null rows).
- **Flush row semantics when flush is no longer last** — its `Ms` must be
  exclusive (table-write only), excluding the state-line snap that follows.
- **Auto-compaction fires at `Count > 4`** — a typical run may never trigger
  it. The integrity sweep must tolerate the post-compact state if it does
  (single winner table, emptied memtable), because reads through the compacted
  path are exactly what M4 will stress.

Phase dependency order (the target):

```
warmup → put → flush → table-get → scan → delete → get-hit → get-miss → integrity
```

---

## 3. Spec — reworked `Run()`

Constants (options threaded from `Program.cs` by you): `SEED` (default `12345`,
`--seed`), `OPS` (default `100_000`, `--ops`). `KEY_LEN = 16` hex chars,
`VALUE_LEN = 100` bytes.

| # | phase | workload | timing note | means |
|---|-------|----------|-------------|-------|
| 0 | warmup | 2k puts, **distinct key space** | untimed, excluded from report | JIT |
| 1 | put | `OPS` puts, RNG-layout keys, `value = ValueOf(key)` | timed; key/value lists kept for later phases | write path (WAL append + memtable) |
| 2 | flush | one `FlushToSSTable()` | exclusive `Ms`, then state-line | table-write cost |
| 3 | table-get | `OPS` reads of existing keys | timed | **disk** path (`TryReadEntry`, bloom-gated) |
| 4 | scan | `SCANS=500` 1k-entry windows over populated range | timed | disk scan + merge |
| 5 | delete | `OPS/2` seeded random subset (recorded as `deleted[]`) | timed | tombstone write path |
| 6 | get-hit | reads over survivors (`writes \ deleted`) | timed | clean hit rate |
| 7 | get-miss | reads over `deleted[]` | timed | clean miss rate |
| 8 | integrity | full sweep, see §4 | timed + counts | corruption detector |

**Determinism (the "mess" that can verify itself):**
- `ValueOf(key) = 100B` from a per-key-seeded RNG (e.g. hash `key` once, then
  stream bytes) — reproducible on re-read.
- Keys come from a seeded RNG producing 16-hex-char strings, deduped into a
  `HashSet` until `writes.Count == OPS` → randomized layout (the mess), not
  sequential, while uniqueness keeps every put an insert.

---

## 4. Integrity sweep spec

Run **last**, over the full accumulated state:

- `for k in writes`: `TryGet(k)` must return `None` and a value **byte-equal** to
  `ValueOf(k)` → else `mismatched-value` / `unexpected-code`.
- `for k in deleted`: `TryGet` must return `KeyWasNotFound` → else `undeleted`
  (a tombstone resurrection — the exact bug compaction can cause).
- Re-verify every key that reported any non-`None` code during timed phases.
- Sweep all keys by default; `--sample N` for very large runs.

The sweep doubles as error visibility: a run whose timed phases produced any
non-`None` exit code, or any mismatch here, is **flagged** — a benchmark run can
be fast *and lying*; this is the only place that statement is checked.

---

## 5. State-line spec

Appended to every phase row's notes, captured **after** the phase (or after the
flush's exclusive timing):

```
mem=<x>MB wal=<x>MB tbl=<x>MB(n) snp=<x>MB
```

| column | source |
|--------|--------|
| `mem` | seam (b): `MemStore`/`Frozen_` payload bytes (logical, not RSS) |
| `wal` | `FileInfo(engine.WALFile).Length` |
| `tbl` | `Σ Directory.GetFiles(FilesPath, "SSTable-*")` lengths + `(n)` table count |
| `snp` | `FileInfo(engine.SnapshotFile).Length` |

---

## 6. Output format

Fixed-width columns so before/after reports diff cleanly:

```
kv-store benchmark        seed=12345  ops=100000  dir=/tmp/kv-bench
phase       ops      ms     ops/sec   state                         errs
put         100000   2078.1 48123.4   mem=0.2M wal=11M tbl=0M(0) snp=0M 0
flush       100000   189.3  —         mem=0M wal=11M tbl=11.2M(1) snp=0M 0
table-get   100000   2100.5 47608.1   mem=0M wal=11M tbl=11.2M(1) snp=0M 0
scan        500      404.8  1234.5    mem=0M wal=11M tbl=11.2M(1) snp=0M 0
delete      50000    1040.2 48067.2   mem=0M wal=11M tbl=11.2M(1) snp=0M 0
get-hit     50000    633.7  78901.2   mem=0M wal=11M tbl=11.2M(1) snp=0M 0
get-miss    50000    640.0  78125.0   mem=0M wal=11M tbl=11.2M(1) snp=0M 0
integrity   331600   812.2  —         errs=0  mism=0  undeleted=0        —
```

- Every phase carries a per-row `errs` count (non-`None` codes observed) instead
  of discarding exit codes.
- `integrity` row carries mismatch/undeleted totals; `errs != 0` or `mism != 0`
  means the run detected corruption — that is the "corruption may occur and that
  would be useful" signal.

---

## 7. Open items

- Seam (b) is a product change → yours to apply. Without it the state-line `mem`
  column stays 0 (or falls back to noisy `GC.GetTotalMemory`).
- `before.txt` capture: run once against the current single-threaded engine with
  the current commit recorded, archive under `docs/benchmarks/`. Every M4 change
  reruns and diffs.