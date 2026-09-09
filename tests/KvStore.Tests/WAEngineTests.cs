using kv_store.Enums;
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
    public void Init_NonExistentDir_ReturnsInvalidPath()
    {
        var engine = new WAEngine();
        var missing = Path.Combine(
            Path.GetTempPath(),
            "kv-store-missing",
            Guid.NewGuid().ToString("N")
        );
        Assert.Equal(ErrorCode.InvalidPath, engine.Init(out var errors, missing));
    }

    [Fact]
    public void Put_TryGet_ReturnsStoredValue()
    {
        var engine = new WAEngine();
        Assert.Equal(ErrorCode.None, engine.Init(out var errors, tempDir));

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
        var engine = new WAEngine();
        Assert.Equal(ErrorCode.None, engine.Init(out var errors, tempDir));
        Assert.Equal(ErrorCode.None, engine.Put("k", [1]));
        Assert.Equal(ErrorCode.None, engine.Put("k", [2, 3]));

        Assert.Equal(ErrorCode.None, engine.TryGet("k", out var v));
        Assert.Equal([2, 3], v);
    }

    [Fact]
    public void TryGet_MissingKey_ReturnsKeyNotFound()
    {
        var engine = new WAEngine();
        Assert.Equal(ErrorCode.None, engine.Init(out var errors, tempDir));
        Assert.Equal(ErrorCode.KeyNotFound, engine.TryGet("nope", out _));
    }

    [Fact]
    public void Delete_RemovesKey()
    {
        var engine = new WAEngine();
        Assert.Equal(ErrorCode.None, engine.Init(out var errors, tempDir));
        Assert.Equal(ErrorCode.None, engine.Put("d", [1]));
        Assert.Equal(ErrorCode.None, engine.Delete("d"));
        Assert.Equal(ErrorCode.KeyNotFound, engine.TryGet("d", out _));
        Assert.Equal(ErrorCode.KeyNotFound, engine.Delete("d"));
    }

    [Fact]
    public void Replay_EmptyLog_ReturnsFileIsEmpty()
    {
        var engine = new WAEngine();
        Assert.Equal(ErrorCode.None, engine.Init(out var errors, tempDir));
        Assert.Equal(ErrorCode.FileIsEmpty, engine.ReplayRecords());
    }

    [Fact]
    public void Replay_PutsAndDeletes_RestoresState()
    {
        var writer = new WAWriter();
        Assert.Equal(ErrorCode.None, writer.Init(Path.Combine(tempDir, "wal_log")));
        Assert.Equal(ErrorCode.None, WARecord.GetRecord(out var p1, WAOperation.PUT, "a", [1]));
        Assert.Equal(ErrorCode.None, writer.Append(p1));
        Assert.Equal(ErrorCode.None, WARecord.GetRecord(out var p2, WAOperation.PUT, "b", [2]));
        Assert.Equal(ErrorCode.None, writer.Append(p2));
        Assert.Equal(ErrorCode.None, WARecord.GetRecord(out var d, WAOperation.DELETE, "a", null));
        Assert.Equal(ErrorCode.None, writer.Append(d));

        var engine = new WAEngine();
        Assert.Equal(ErrorCode.None, engine.Init(out var errors, tempDir));
        Assert.Equal(ErrorCode.None, engine.ReplayRecords());

        Assert.Equal(ErrorCode.KeyNotFound, engine.TryGet("a", out _));
        Assert.Equal(ErrorCode.None, engine.TryGet("b", out var vb));
        Assert.Equal([2], vb);
    }

    [Fact]
    public void SaveSnapshot_ThenLoadSnapshot_NewEngine_RestoresState()
    {
        var engine = new WAEngine();
        Assert.Equal(ErrorCode.None, engine.Init(out var errors, tempDir));
        Assert.Equal(ErrorCode.None, engine.Put("k1", [5]));
        Assert.Equal(ErrorCode.None, engine.Put("k2", [6, 7]));

        var snapPath = Path.Combine(tempDir, "snapshot.dat");
        File.Create(snapPath).Dispose();
        Assert.Equal(ErrorCode.None, engine.SaveSnapshot(snapPath));

        var engine2 = new WAEngine();
        Assert.Equal(ErrorCode.None, engine2.Init(out errors, tempDir));
        Assert.Equal(ErrorCode.None, engine2.LoadSnapshot(snapPath));

        Assert.Equal(ErrorCode.None, engine2.TryGet("k1", out var v1));
        Assert.Equal([5], v1);
        Assert.Equal(ErrorCode.None, engine2.TryGet("k2", out var v2));
        Assert.Equal([6, 7], v2);
    }

    [Fact]
    public void SaveSnapshot_TruncatesWAL()
    {
        var engine = new WAEngine();
        Assert.Equal(ErrorCode.None, engine.Init(out var errors, tempDir));
        Assert.Equal(ErrorCode.None, engine.Put("x", [1]));

        var snapPath = Path.Combine(tempDir, "snapshot.dat");
        File.Create(snapPath).Dispose();
        Assert.Equal(ErrorCode.None, engine.SaveSnapshot(snapPath));

        // WAL now empty, so replay finds nothing
        Assert.Equal(ErrorCode.FileIsEmpty, engine.ReplayRecords());
    }

    [Fact]
    public void Init_CorruptTable_QuarantinesAndReportsError()
    {
        var table = Path.Combine(tempDir, "SSTable-00001");
        File.WriteAllBytes(table, [0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03]);

        var engine = new WAEngine();
        Assert.Equal(ErrorCode.ErrorInSSTablesLoading, engine.Init(out var errors, tempDir));

        var entry = Assert.Single(errors);
        Assert.Equal(ErrorCode.FileCorruptedOrUnsupportedVersion, entry.e);
        Assert.Equal(table, entry.f);
        Assert.False(File.Exists(table));
        Assert.True(File.Exists(table + ".corrupt"));
    }

    [Fact]
    public void Init_TruncatedTable_QuarantinesAndReportsError()
    {
        var table = Path.Combine(tempDir, "SSTable-00002");
        File.WriteAllBytes(table, [0x53, 0x53, 0x54, 0x01, .. new byte[10]]);

        var engine = new WAEngine();
        Assert.Equal(ErrorCode.ErrorInSSTablesLoading, engine.Init(out var errors, tempDir));

        var entry = Assert.Single(errors);
        Assert.Equal(ErrorCode.IOError, entry.e);
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

        var engine = new WAEngine();
        Assert.Equal(ErrorCode.CorruptedEntry, engine.Init(out var errors, tempDir));

        var entry = Assert.Single(errors);
        Assert.Equal(ErrorCode.CorruptedEntry, entry.e);
        Assert.Equal(table, entry.f);
        Assert.True(File.Exists(table)); // not renamed
    }

    [Fact]
    public void Init_OneGoodOneCorrupt_LoadsGoodTable()
    {
        var entries = new List<KeyValuePair<string, byte[]>>();
        for (int i = 0; i < 20; i++)
            entries.Add(new($"key-{i:D3}", [(byte)i]));

        var good = Path.Combine(tempDir, "SSTable-00010");
        Assert.Equal(
            ErrorCode.None,
            SSTable.WriteTableToFile(good, entries, entries.Count, out _)
        );

        var bad = Path.Combine(tempDir, "SSTable-00020");
        File.WriteAllBytes(bad, [0xDE, 0xAD, 0xBE, 0xEF, 0x01]);

        var engine = new WAEngine();
        Assert.Equal(ErrorCode.ErrorInSSTablesLoading, engine.Init(out var errors, tempDir));

        var entry = Assert.Single(errors);
        Assert.Equal(ErrorCode.FileCorruptedOrUnsupportedVersion, entry.e);
        Assert.Equal(bad, entry.f);

        // corrupt one quarantined, good table still reachable
        Assert.True(File.Exists(bad + ".corrupt"));
        Assert.Equal(ErrorCode.None, engine.TryGet("key-000", out var v));
        Assert.Equal([0], v);
    }

    [Fact]
    public void Flush_OverThreshold_TriggersPersistentTable()
    {
        var engine = new WAEngine();
        Assert.Equal(ErrorCode.None, engine.Init(out var errors, tempDir));

        var big = new byte[40_000];
        Random.Shared.NextBytes(big);

        // 40KB payload crosses flushThresholdBytes -> auto-flush
        Assert.Equal(ErrorCode.None, engine.Put("big", big));
        Assert.Equal(ErrorCode.None, engine.TryGet("big", out var v));
        Assert.Equal(big, v);

        // table survives restart: catalog scan + TryGet from immutableSSTables
        var engine2 = new WAEngine();
        Assert.Equal(ErrorCode.None, engine2.Init(out errors, tempDir));
        Assert.Equal(ErrorCode.None, engine2.TryGet("big", out var v2));
        Assert.Equal(big, v2);
    }

    [Fact]
    public void Uninitialized_Ops_ReturnUnInitialized()
    {
        var engine = new WAEngine();
        Assert.Equal(ErrorCode.UnInitializedInstance, engine.Put("a", [1]));
        Assert.Equal(ErrorCode.UnInitializedInstance, engine.TryGet("a", out _));
        Assert.Equal(ErrorCode.UnInitializedInstance, engine.Delete("a"));
        Assert.Equal(ErrorCode.UnInitializedInstance, engine.ReplayRecords());
    }
}
