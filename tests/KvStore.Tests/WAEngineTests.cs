using kv_store.EnumsAndConstants;
using kv_store.Implementations;
using Xunit;

namespace KvStore.Tests;

public class WAEngineTests : IDisposable
{
    readonly string tempDir;

    public WAEngineTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "kv-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(tempDir, recursive: true);
        }
        catch
        { /* best effort */
        }
    }

    [Fact]
    public void Init_AutoCreatesDirectory()
    {
        var engine = new WAEngine(Path.Combine(tempDir, "nested", "data"));
        Assert.Equal(ErrorCode.None, engine.Init(out _));
        Assert.True(Directory.Exists(Path.Combine(tempDir, "nested", "data")));
    }

    [Fact]
    public void Put_TryGet_ReturnsStoredValue()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));

        Assert.Equal(ErrorCode.None, engine.Put("a", [1, 2]));
        Assert.Equal(ErrorCode.None, engine.Put("b", "hi"u8.ToArray()));

        Assert.Equal(ErrorCode.None, engine.TryGet("a", out var va));
        Assert.Equal([1, 2], va);
        Assert.Equal(ErrorCode.None, engine.TryGet("b", out var vb));
        Assert.Equal("hi"u8.ToArray(), vb);
    }

    [Fact]
    public void Put_OverwritesExisting()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));
        Assert.Equal(ErrorCode.None, engine.Put("k", [1]));
        Assert.Equal(ErrorCode.None, engine.Put("k", [2, 3]));

        Assert.Equal(ErrorCode.None, engine.TryGet("k", out var v));
        Assert.Equal([2, 3], v);
    }

    [Fact]
    public void TryGet_MissingKey_ReturnsKeyNotFound()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));
        Assert.Equal(ErrorCode.KeyWasNotFound, engine.TryGet("nope", out _));
    }

    [Fact]
    public void Delete_RemovesKey()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));
        Assert.Equal(ErrorCode.None, engine.Put("d", [1]));
        Assert.Equal(ErrorCode.None, engine.Delete("d"));
        Assert.Equal(ErrorCode.KeyWasNotFound, engine.TryGet("d", out _));
        Assert.Equal(ErrorCode.None, engine.Delete("d")); // idempotent
    }

    [Fact]
    public void Replay_EmptyLog_ReturnsFileIsEmpty()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));
        Assert.Equal(ErrorCode.FileIsEmpty, engine.ReplayRecords());
    }

    [Fact]
    public void Replay_PutsAndDeletes_RestoresState()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));
        Assert.Equal(ErrorCode.None, engine.Put("a", [1]));
        Assert.Equal(ErrorCode.None, engine.Put("b", [2]));
        Assert.Equal(ErrorCode.None, engine.Delete("a"));

        var engine2 = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine2.Init(out _));
        Assert.Equal(ErrorCode.None, engine2.ReplayRecords());

        Assert.Equal(ErrorCode.KeyWasNotFound, engine2.TryGet("a", out _));
        Assert.Equal(ErrorCode.None, engine2.TryGet("b", out var vb));
        Assert.Equal([2], vb);
    }

    [Fact]
    public void SaveSnapshot_ThenLoadSnapshot_NewEngine_RestoresState()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));
        Assert.Equal(ErrorCode.None, engine.Put("k1", [5]));
        Assert.Equal(ErrorCode.None, engine.Put("k2", [6, 7]));

        Assert.Equal(ErrorCode.None, engine.SaveSnapshot());

        var engine2 = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine2.Init(out _));
        Assert.Equal(ErrorCode.None, engine2.LoadSnapshot());

        Assert.Equal(ErrorCode.None, engine2.TryGet("k1", out var v1));
        Assert.Equal([5], v1);
        Assert.Equal(ErrorCode.None, engine2.TryGet("k2", out var v2));
        Assert.Equal([6, 7], v2);
    }

    [Fact]
    public void SaveSnapshot_TruncatesWAL()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));
        Assert.Equal(ErrorCode.None, engine.Put("x", [1]));

        Assert.Equal(ErrorCode.None, engine.SaveSnapshot());

        // WAL now empty, so replay finds nothing
        Assert.Equal(ErrorCode.FileIsEmpty, engine.ReplayRecords());
    }

    [Fact]
    public void Init_CorruptTable_QuarantinesAndReportsError()
    {
        var table = Path.Combine(tempDir, "SSTable-00001");
        File.WriteAllBytes(table, [0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03]);

        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.SstablesFailedToLoad, engine.Init(out var errors));

        var entry = Assert.Single(errors);
        Assert.Equal(ErrorCode.FileIsCorruptedOrVersionUnsupported, entry.e);
        Assert.Equal(table, entry.f);
        Assert.False(File.Exists(table));
        Assert.True(File.Exists(table + ".corrupt"));
    }

    [Fact]
    public void Init_TruncatedTable_QuarantinesAndReportsError()
    {
        var table = Path.Combine(tempDir, "SSTable-00002");
        File.WriteAllBytes(table, [0x53, 0x53, 0x54, 0x01, .. new byte[10]]);

        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.SstablesFailedToLoad, engine.Init(out var errors));

        var entry = Assert.Single(errors);
        Assert.Equal(ErrorCode.InputOutputFailed, entry.e);
        Assert.Equal(table, entry.f);
        Assert.False(File.Exists(table));
        Assert.True(File.Exists(table + ".corrupt"));
    }

    [Fact]
    public void Init_UnknownCorruption_AbortsWithoutQuarantine()
    {
        // header magic valid, but a truncated/incoherent sparse-index region
        // (IndexLength claims to extend past EOF) -> CorruptedEntry, which is not
        // quarantined: load aborts and the file is left in place.
        var table = Path.Combine(tempDir, "SSTable-00003");
        using (var fs = File.Create(table))
        using (var w = new BinaryWriter(fs))
        {
            w.Write([0x53, 0x53, 0x54, 0x01]); // valid header magic
            w.Write("k"); // one index entry: key
            w.Write(0L); // ... offset
            // footer: IndexOffset before those entries, IndexLength past EOF
            w.Write(4L); // IndexOffset
            w.Write(int.MaxValue); // IndexLength
            w.Write(0L); // FilterOffset
            w.Write(0); // FilterLength
            w.Write(0L); // BitSize
            w.Write(0); // HashCount
            w.Write(0L); // FirstLastKeyOffset
            w.Write(1); // RecordCount
            w.Write([0x53, 0x53, 0x54, 0x01]); // tail magic
        }

        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.EntryIsCorrupted, engine.Init(out var errors));

        var entry = Assert.Single(errors);
        Assert.Equal(ErrorCode.EntryIsCorrupted, entry.e);
        Assert.Equal(table, entry.f);
        Assert.True(File.Exists(table)); // not renamed
    }

    [Fact]
    public void Init_OneGoodOneCorrupt_LoadsGoodTable()
    {
        var entries = new List<KeyValuePair<string, byte[]?>>();
        for (int i = 0; i < 20; i++)
            entries.Add(new($"key-{i:D3}", [(byte)i]));

        var good = Path.Combine(tempDir, "SSTable-00010");
        Assert.Equal(
            ErrorCode.None,
            SSTable.WriteTableToFile(
                good,
                new ImmutableSkipList<string, byte[]?>(entries),
                entries.Count,
                out _
            )
        );

        var bad = Path.Combine(tempDir, "SSTable-00020");
        File.WriteAllBytes(bad, [0xDE, 0xAD, 0xBE, 0xEF, 0x01]);

        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.SstablesFailedToLoad, engine.Init(out var errors));

        var (e, f) = Assert.Single(errors);
        Assert.Equal(ErrorCode.FileIsCorruptedOrVersionUnsupported, e);
        Assert.Equal(bad, f);

        // corrupt one quarantined, good table still reachable
        Assert.True(File.Exists(bad + ".corrupt"));
        Assert.Equal(ErrorCode.None, engine.TryGet("key-000", out var v));
        Assert.Equal([0], v);
    }

    [Fact]
    public void Flush_OverThreshold_TriggersPersistentTable()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));

        var big = new byte[40_000];
        Random.Shared.NextBytes(big);

        // 40KB payload crosses flushThresholdBytes -> auto-flush
        Assert.Equal(ErrorCode.None, engine.Put("big", big));
        Assert.Equal(ErrorCode.None, engine.TryGet("big", out var v));
        Assert.Equal(big, v);

        // table survives restart: catalog scan + TryGet from immutableSSTables
        var engine2 = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine2.Init(out _));
        Assert.Equal(ErrorCode.None, engine2.TryGet("big", out var v2));
        Assert.Equal(big, v2);
    }

    [Fact]
    public void Flush_Repeated_ProducesDistinctTables_NoTmpLeftover()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));

        Assert.Equal(ErrorCode.None, engine.Put("a", [1]));
        Assert.Equal(ErrorCode.None, engine.FlushToSSTable());
        Assert.Equal(ErrorCode.None, engine.Put("b", [2]));
        Assert.Equal(ErrorCode.None, engine.FlushToSSTable());

        var tables = Directory.GetFiles(tempDir, "SSTable-*").Select(Path.GetFileName).ToList();
        Assert.DoesNotContain("SSTable-TMP", tables);
        // two distinct numbered tables, monotonic serials (no -00000 collision)
        Assert.Equal(
            2,
            tables.Count(f => System.Text.RegularExpressions.Regex.IsMatch(f!, @"^SSTable-\d{5}$"))
        );
    }

    [Fact]
    public void Flush_TwoTablesForSameKey_NewestTableWins()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));

        var v1 = new byte[40_000];
        Random.Shared.NextBytes(v1);
        var v2 = new byte[40_000];
        Random.Shared.NextBytes(v2);

        // each 40KB put crosses the flush threshold -> "k" lands in two SSTables
        Assert.Equal(ErrorCode.None, engine.Put("k", v1));
        Assert.Equal(ErrorCode.None, engine.Put("k", v2));

        Assert.Equal(ErrorCode.None, engine.TryGet("k", out var current));
        Assert.Equal(v2, current);

        // newest table must also win after restart
        var engine2 = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine2.Init(out _));
        Assert.Equal(ErrorCode.None, engine2.TryGet("k", out var reloaded));
        Assert.Equal(v2, reloaded);
    }

    [Fact]
    public void Delete_FlushedKey_IsHonored_WhenNotInMemory()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));

        var big = new byte[40_000];
        Random.Shared.NextBytes(big);
        Assert.Equal(ErrorCode.None, engine.Put("k", big)); // flushed to SSTable

        engine.Delete("k"); // key only exists on disk, not in memory
        Assert.Equal(ErrorCode.KeyWasNotFound, engine.TryGet("k", out _));
    }

    [Fact]
    public void Flush_WithTombstone_CreatesDeleteFrame_SurvivesRestart()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));
        Assert.Equal(ErrorCode.None, engine.Put("a", [1]));
        Assert.Equal(ErrorCode.None, engine.Put("b", [2]));
        Assert.Equal(ErrorCode.None, engine.Delete("a"));

        var big = new byte[40_000];
        Random.Shared.NextBytes(big);
        Assert.Equal(ErrorCode.None, engine.Put("big", big)); // flush includes a-tombstone

        var engine2 = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine2.Init(out _));
        Assert.Equal(ErrorCode.KeyWasNotFound, engine2.TryGet("a", out _));
        Assert.Equal(ErrorCode.None, engine2.TryGet("b", out var vb));
        Assert.Equal([2], vb);
        Assert.Equal(ErrorCode.None, engine2.TryGet("big", out var vBig));
        Assert.Equal(big, vBig);
    }

    static List<KeyValuePair<string, byte[]>> Scan(WAEngine engine, string startKey, string endKey)
    {
        Assert.Equal(ErrorCode.None, engine.Scan(startKey, endKey, out var results));
        return [.. results];
    }

    [Fact]
    public void Scan_EmptyStore_ReturnsEmpty()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));
        Assert.Empty(Scan(engine, "a", "z"));
    }

    [Fact]
    public void Scan_MemstoreEntries_ReturnsSortedInRange()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));
        Assert.Equal(ErrorCode.None, engine.Put("a", [1]));
        Assert.Equal(ErrorCode.None, engine.Put("b", [2]));
        Assert.Equal(ErrorCode.None, engine.Put("c", [3]));
        Assert.Equal(ErrorCode.None, engine.Put("d", [4]));

        var results = Scan(engine, "b", "c");
        Assert.Equal(2, results.Count);
        Assert.Equal("b", results[0].Key);
        Assert.Equal([2], results[0].Value);
        Assert.Equal("c", results[1].Key);
        Assert.Equal([3], results[1].Value);
    }

    [Fact]
    public void Scan_InclusiveRangeBoundaries()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));
        Assert.Equal(ErrorCode.None, engine.Put("m", [1]));

        var results = Scan(engine, "m", "m");
        Assert.Equal([new KeyValuePair<string, byte[]>("m", [1])], results);
    }

    [Fact]
    public void Scan_ExcludesTombstones()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));
        Assert.Equal(ErrorCode.None, engine.Put("a", [1]));
        Assert.Equal(ErrorCode.None, engine.Put("b", [2]));
        Assert.Equal(ErrorCode.None, engine.Delete("b"));

        var results = Scan(engine, "a", "z");
        var entry = Assert.Single(results);
        Assert.Equal("a", entry.Key);
    }

    [Fact]
    public void Scan_NewestValueWins()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));
        Assert.Equal(ErrorCode.None, engine.Put("k", [1]));
        Assert.Equal(ErrorCode.None, engine.Put("k", [2, 3]));

        var results = Scan(engine, "a", "z");
        var entry = Assert.Single(results);
        Assert.Equal("k", entry.Key);
        Assert.Equal([2, 3], entry.Value);
    }

    [Fact]
    public void Scan_StartGreaterThanEnd_ReturnsEmpty()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));
        Assert.Equal(ErrorCode.None, engine.Put("a", [1]));

        Assert.Empty(Scan(engine, "z", "a"));
    }

    [Fact]
    public void Scan_AfterFlush_IncludesSSTableEntries()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));

        Assert.Equal(ErrorCode.None, engine.Put("a", [1]));
        Assert.Equal(ErrorCode.None, engine.Put("b", [2]));

        var big = new byte[40_000];
        Random.Shared.NextBytes(big);
        Assert.Equal(ErrorCode.None, engine.Put("zz", big)); // flush

        var results = Scan(engine, "a", "z");
        Assert.Equal(2, results.Count);
        Assert.Equal("a", results[0].Key);
        Assert.Equal("b", results[1].Key);
    }

    [Fact]
    public void Scan_IncludesSSTableEntries_NewestTableWins()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));

        Assert.Equal(ErrorCode.None, engine.Put("a", [1]));
        Assert.Equal(ErrorCode.None, engine.Put("b", [5]));
        var big1 = new byte[40_000];
        Random.Shared.NextBytes(big1);
        Assert.Equal(ErrorCode.None, engine.Put("zz", big1)); // flush table 1 {a:1, b:5}

        Assert.Equal(ErrorCode.None, engine.Put("a", [2]));
        var big2 = new byte[40_000];
        Random.Shared.NextBytes(big2);
        Assert.Equal(ErrorCode.None, engine.Put("yy", big2)); // flush table 2 {a:2}

        // "a" flushed into both tables -> newest table wins; "b" exists only in table 1 -> still visible
        var results = Scan(engine, "a", "b");
        Assert.Equal(2, results.Count);
        Assert.Equal("a", results[0].Key);
        Assert.Equal([2], results[0].Value); // newest table wins
        Assert.Equal("b", results[1].Key);
        Assert.Equal([5], results[1].Value); // older table still visible
    }

    [Fact]
    public void Scan_TombstoneShadowsFlushedKey()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.None, engine.Init(out _));

        Assert.Equal(ErrorCode.None, engine.Put("a", [1]));
        var big = new byte[40_000];
        Random.Shared.NextBytes(big);
        Assert.Equal(ErrorCode.None, engine.Put("zz", big)); // "a" flushed

        Assert.Equal(ErrorCode.None, engine.Delete("a")); // tombstone shadows flushed "a"
        Assert.Equal(ErrorCode.None, engine.Put("b", [2]));

        // "a" flushed then deleted -> excluded; "b" in memstore -> included; "zz" out of range
        var results = Scan(engine, "a", "z");
        var entry = Assert.Single(results);
        Assert.Equal("b", entry.Key);
    }

    [Fact]
    public void Scan_Uninitialized_ReturnsInstanceNotInitialized()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.InstanceIsNotInitialized, engine.Scan("a", "z", out _));
    }

    [Fact]
    public void Uninitialized_Ops_ReturnUnInitialized()
    {
        var engine = new WAEngine(tempDir);
        Assert.Equal(ErrorCode.InstanceIsNotInitialized, engine.Put("a", [1]));
        Assert.Equal(ErrorCode.InstanceIsNotInitialized, engine.TryGet("a", out _));
        Assert.Equal(ErrorCode.InstanceIsNotInitialized, engine.Delete("a"));
        Assert.Equal(ErrorCode.InstanceIsNotInitialized, engine.ReplayRecords());
    }
}
