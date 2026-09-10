using kv_store.Enums;
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

        Assert.Equal(ErrorCode.None, SSTable.ReadFileToTable(path, out var table));
        Assert.NotNull(table);
        Assert.Equal(25, table.RecordCount_);
        Assert.Equal("key-000", table.FirstKey_);
        Assert.Equal("key-024", table.LastKey_);
        Assert.Equal(path, table.FileName_);

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
        Assert.Equal(ErrorCode.None, SSTable.ReadFileToTable(path, out var table));

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        // stride index: one entry per 10 records
        foreach (var indexEntry in table!.SparseIndex_)
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
            ErrorCode.FileCorruptedOrUnsupportedVersion,
            SSTable.ReadFileToTable(path, out _)
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
            ErrorCode.FileCorruptedOrUnsupportedVersion,
            SSTable.ReadFileToTable(path, out _)
        );
    }

    [Fact]
    public void ReadFileToTable_TruncatedFile_ReturnsIOError()
    {
        var path = Path.Combine(tempDir, "SSTable-00012");
        File.WriteAllBytes(path, [0x53, 0x53, 0x54, 0x01, .. new byte[30]]);

        Assert.Equal(ErrorCode.IOError, SSTable.ReadFileToTable(path, out _));
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
        Assert.Equal(ErrorCode.None, SSTable.ReadFileToTable(path, out var table));

        foreach (var k in new[] { "k-000", "k-001", "k-009", "k-010", "k-015", "k-024" })
        {
            var e = table!.TryReadEntry(k, out _);
            Assert.True(e == ErrorCode.None, $"{k}: {e}");
        }
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
        Assert.Equal(ErrorCode.None, SSTable.ReadFileToTable(path, out var table));

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

        Assert.Equal(ErrorCode.KeyDeleted, table!.TryReadEntry("k-001", out _));
        Assert.Equal(ErrorCode.None, table.TryReadEntry("k-000", out var v0));
        Assert.Equal([1], v0);
        Assert.Equal(ErrorCode.None, table.TryReadEntry("k-002", out var v2));
        Assert.Equal([2], v2);
    }
}
