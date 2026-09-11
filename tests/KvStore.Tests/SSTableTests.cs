using kv_store.EnumsAndConstants;
using kv_store.Implementations;
using Xunit;

namespace KvStore.Tests;

public class SSTableTests : IDisposable
{
    readonly string tempDir;

    public SSTableTests()
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

    static List<KeyValuePair<string, byte[]?>> MakeEntries(int count)
    {
        var list = new List<KeyValuePair<string, byte[]?>>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(new($"key-{i:D3}", [(byte)i]));
        }
        return list;
    }

    [Fact]
    public void WriteTableToFile_ThenReadFileToTable_RoundTripsAllRecords()
    {
        var entries = MakeEntries(25);
        var path = Path.Combine(tempDir, "SSTable-00001");

        Assert.Equal(
            ErrorCode.None,
            SSTable.WriteTableToFile(path, new(entries), entries.Count, out var written)
        );

        Assert.Equal(ErrorCode.None, SSTable.ReadFromFileToTable(path, out var table));
        Assert.NotNull(table);
        Assert.Equal(25, table.RecordCount);
        Assert.Equal("key-000", table.FirstKey);
        Assert.Equal("key-024", table.LastKey);
        Assert.Equal(path, table.FileName);

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        reader.ReadBytes(4); // header magic

        for (int i = 0; i < entries.Count; i++)
        {
            Assert.Equal(
                ErrorCode.None,
                WARecord.ReadFrame(reader, out _, out var key, out var value)
            );
            Assert.Equal(entries[i].Key, key);
            Assert.Equal(entries[i].Value, value);
        }
    }

    [Fact]
    public void WriteTableToFile_SparseIndexOffsetsPointAtRecordStarts()
    {
        var entries = MakeEntries(25);
        var path = Path.Combine(tempDir, "SSTable-00002");

        Assert.Equal(
            ErrorCode.None,
            SSTable.WriteTableToFile(path, new(entries), entries.Count, out _)
        );
        Assert.Equal(ErrorCode.None, SSTable.ReadFromFileToTable(path, out var table));

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        // stride index: one entry per 10 records
        foreach (var indexEntry in table!.SparseIndex)
        {
            var expectedIdx = int.Parse(indexEntry.Key[4..]); // key-xxx -> xxx

            reader.BaseStream.Seek(indexEntry.Value, SeekOrigin.Begin);
            Assert.Equal(
                ErrorCode.None,
                WARecord.ReadFrame(reader, out _, out var idxKey, out var idxValue)
            );
            Assert.Equal($"key-{expectedIdx:D3}", idxKey);
            Assert.Equal(new byte[] { (byte)expectedIdx }, idxValue);
        }
    }

    [Fact]
    public void WriteTableToFile_EmptyEntries_ReturnsEntryIsEmpty()
    {
        var path = Path.Combine(tempDir, "SSTable-empty");
        Assert.Equal(ErrorCode.EntryIsEmpty, SSTable.WriteTableToFile(path, [], 0, out _));
    }

    [Fact]
    public void ReadFileToTable_BadHeaderMagic_ReturnsFileCorrupted()
    {
        var path = Path.Combine(tempDir, "SSTable-00010");
        File.WriteAllBytes(path, [0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03]);

        Assert.Equal(
            ErrorCode.FileIsCorruptedOrVersionUnsupported,
            SSTable.ReadFromFileToTable(path, out _)
        );
    }

    [Fact]
    public void ReadFileToTable_CorruptedTailMagic_ReturnsFileCorrupted()
    {
        var entries = MakeEntries(5);
        var path = Path.Combine(tempDir, "SSTable-00011");
        Assert.Equal(
            ErrorCode.None,
            SSTable.WriteTableToFile(path, new(entries), entries.Count, out _)
        );

        var bytes = File.ReadAllBytes(path);
        bytes[^1] = (byte)~bytes[^1]; // flip last byte of tail magic
        File.WriteAllBytes(path, bytes);

        Assert.Equal(
            ErrorCode.FileIsCorruptedOrVersionUnsupported,
            SSTable.ReadFromFileToTable(path, out _)
        );
    }

    [Fact]
    public void ReadFileToTable_TruncatedFile_ReturnsIOError()
    {
        var path = Path.Combine(tempDir, "SSTable-00012");
        File.WriteAllBytes(path, [0x53, 0x53, 0x54, 0x01, .. new byte[30]]);

        Assert.Equal(ErrorCode.InputOutputFailed, SSTable.ReadFromFileToTable(path, out _));
    }

    [Fact]
    public void Scratch_NonStrideKey_Readable()
    {
        var pairs = new List<KeyValuePair<string, byte[]?>>();
        for (int i = 0; i < 25; i++)
            pairs.Add(new($"k-{i:D3}", [(byte)i]));
        var path = Path.Combine(tempDir, "SSTable-00001");
        Assert.Equal(
            ErrorCode.None,
            SSTable.WriteTableToFile(path, new(pairs), pairs.Count, out _)
        );
        Assert.Equal(ErrorCode.None, SSTable.ReadFromFileToTable(path, out var table));

        foreach (var k in new[] { "k-000", "k-001", "k-009", "k-010", "k-015", "k-024" })
        {
            var e = table!.TryReadEntry(k, out _);
            Assert.True(e == ErrorCode.None, $"{k}: {e}");
        }
    }

    [Fact]
    public void WriteTableToFile_OutTable_IsQueryableImmediately()
    {
        var pairs = new List<KeyValuePair<string, byte[]?>>();
        for (int i = 0; i < 25; i++)
            pairs.Add(new($"k-{i:D3}", [(byte)i]));
        pairs.Add(new("gone", null)); // tombstone

        var path = Path.Combine(tempDir, "SSTable-00001");
        Assert.Equal(
            ErrorCode.None,
            SSTable.WriteTableToFile(path, new(pairs), pairs.Count, out var table)
        );

        // the write path constructs the immutable side from the in-memory
        // sections it just wrote; the object must be usable directly (no
        // dependence on a re-read from disk)
        Assert.NotNull(table);
        Assert.Equal(pairs.Count, table.RecordCount);
        Assert.Equal("gone", table.FirstKey); // sorts before k-000
        Assert.Equal("k-024", table.LastKey);
        Assert.Equal(3, table.SparseIndex.Count); // stride 10: i = 0, 10, 20
        Assert.Equal(ErrorCode.None, table.TryReadEntry("k-000", out _));
        Assert.Equal(ErrorCode.None, table.TryReadEntry("k-009", out var v9));
        Assert.Equal([9], v9);
        Assert.Equal(ErrorCode.KeyWasDeleted, table.TryReadEntry("gone", out _));
    }

    [Fact]
    public void TryReadEntry_InRangeAbsentKey_ReturnsKeyNotFound()
    {
        var pairs = new List<KeyValuePair<string, byte[]?>>();
        for (int i = 0; i < 25; i++)
            pairs.Add(new($"k-{i:D3}", [(byte)i]));
        var path = Path.Combine(tempDir, "SSTable-00001");
        Assert.Equal(
            ErrorCode.None,
            SSTable.WriteTableToFile(path, new(pairs), pairs.Count, out _)
        );

        // Force bloom to say "maybe" for the absent key so the scan actually
        // walks past it, exercising the mismatch branch deterministically.
        var bytes = File.ReadAllBytes(path);
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms);
        ms.Position = bytes.Length - 52; // footer, fixed size
        r.ReadInt64(); // IndexOffset
        r.ReadInt32(); // IndexLength
        long filterOffset = r.ReadInt64();
        int filterLength = r.ReadInt32();

        var seed = new BloomFilter(pairs.Count);
        Assert.Equal(ErrorCode.None, seed.Add("k-005a"));
        var mask = seed.GetBytes!;
        for (int i = 0; i < filterLength; i++)
            bytes[filterOffset + i] |= mask[i];
        File.WriteAllBytes(path, bytes);

        Assert.Equal(ErrorCode.None, SSTable.ReadFromFileToTable(path, out var table));

        // in range, bloom passes, sparse lands at k-000, scan stops at k-006 -> legit miss
        Assert.Equal(ErrorCode.KeyWasNotFound, table!.TryReadEntry("k-005a", out _));
    }

    [Fact]
    public void Tombstone_NullValue_StoredAsDeleteFrame_AndReadAsKeyDeleted()
    {
        var pairs = new List<KeyValuePair<string, byte[]?>>
        {
            new("k-000", [1]),
            new("k-001", null),
            new("k-002", [2]),
        };
        var path = Path.Combine(tempDir, "SSTable-00020");
        Assert.Equal(
            ErrorCode.None,
            SSTable.WriteTableToFile(path, new(pairs), pairs.Count, out _)
        );
        Assert.Equal(ErrorCode.None, SSTable.ReadFromFileToTable(path, out var table));

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        reader.ReadBytes(4); // header magic
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(
                ErrorCode.None,
                WARecord.ReadFrame(reader, out var op, out var key, out _)
            );
            Assert.Equal(key == "k-001" ? WAOperation.DELETE : WAOperation.PUT, op);
        }

        Assert.Equal(ErrorCode.KeyWasDeleted, table!.TryReadEntry("k-001", out _));
        Assert.Equal(ErrorCode.None, table.TryReadEntry("k-000", out var v0));
        Assert.Equal([1], v0);
        Assert.Equal(ErrorCode.None, table.TryReadEntry("k-002", out var v2));
        Assert.Equal([2], v2);
    }

    static List<KeyValuePair<string, byte[]?>> MakeScanPairs()
    {
        return
        [
            new("alpha", [1]),
            new("beta", [2]),
            new("gamma", [3]),
            new("zz", [4]),
        ];
    }

    [Fact]
    public void Scan_InclusiveEndKey_IncludesEqualBoundary()
    {
        var pairs = MakeScanPairs();
        var path = Path.Combine(tempDir, "SSTable-00001");
        Assert.Equal(ErrorCode.None, SSTable.WriteTableToFile(path, new(pairs), pairs.Count, out _));
        Assert.Equal(ErrorCode.None, SSTable.ReadFromFileToTable(path, out var table));

        Assert.Equal(ErrorCode.None, table!.Scan("alpha", "beta", out var scanResults));
        List<KeyValuePair<string, byte[]?>> rows = [.. scanResults];
        Assert.Equal(2, rows.Count);
        Assert.Equal("alpha", rows[0].Key);
        Assert.Equal("beta", rows[1].Key);
    }

    [Fact]
    public void Scan_StartKeyInsidePage_ExcludesRecordsBeforeStart()
    {
        var pairs = MakeScanPairs();
        var path = Path.Combine(tempDir, "SSTable-00001");
        Assert.Equal(ErrorCode.None, SSTable.WriteTableToFile(path, new(pairs), pairs.Count, out _));
        Assert.Equal(ErrorCode.None, SSTable.ReadFromFileToTable(path, out var table));

        // sparse index seeks to "alpha" (only entry), but "alpha" < "beta" is out of range
        Assert.Equal(ErrorCode.None, table!.Scan("beta", "zzz", out var scanResults));
        List<KeyValuePair<string, byte[]?>> rows = [.. scanResults];
        Assert.Equal(3, rows.Count);
        Assert.Equal("beta", rows[0].Key);
        Assert.Equal("gamma", rows[1].Key);
        Assert.Equal("zz", rows[2].Key);
    }

    [Fact]
    public void Scan_InRangeTombstone_ComesThroughAsDeleted()
    {
        var pairs = new List<KeyValuePair<string, byte[]?>>
        {
            new("a", [1]),
            new("b", [2]),
            new("gone", null),
        };
        var path = Path.Combine(tempDir, "SSTable-00001");
        Assert.Equal(ErrorCode.None, SSTable.WriteTableToFile(path, new(pairs), pairs.Count, out _));
        Assert.Equal(ErrorCode.None, SSTable.ReadFromFileToTable(path, out var table));

        Assert.Equal(ErrorCode.None, table!.Scan("a", "zz", out var results));
        List<KeyValuePair<string, byte[]?>> rows = [.. results];
        Assert.Equal(3, rows.Count);
        Assert.Equal("a", rows[0].Key);
        Assert.NotNull(rows[0].Value);
        Assert.Equal("b", rows[1].Key);
        Assert.NotNull(rows[1].Value);
        // tombstone row must surface as a null value so the engine's merge
        // sees the key as deleted and can shadow older stores
        Assert.Equal("gone", rows[2].Key);
        Assert.Null(rows[2].Value);
    }
}
