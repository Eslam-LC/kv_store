using kv_store.EnumsAndConstants;
using kv_store.Implementations;
using Xunit;

namespace KvStore.Tests;

public class WAWriterReaderTests : IDisposable
{
    readonly string tempDir;
    readonly string logPath;

    public WAWriterReaderTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "kv-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        logPath = Path.Combine(tempDir, "wal_log");
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

    void AppendFrame(WAOperation op, string key, byte[] value)
    {
        using var fs = new FileStream(logPath, FileMode.Append, FileAccess.Write);
        using var w = new BinaryWriter(fs);
        Assert.Equal(ErrorCode.None, WARecord.WriteFrame(w, op, key, value));
    }

    ErrorCode ReadRecords(KeyValueStore store)
    {
        using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read);
        using var r = new BinaryReader(fs);
        return WAReader.ReadRecords(r, store);
    }

    [Fact]
    public void Read_EmptyLog_ReturnsFileIsEmpty()
    {
        File.Create(logPath).Dispose();
        Assert.Equal(ErrorCode.FileIsEmpty, ReadRecords(new KeyValueStore()));
    }

    [Fact]
    public void Read_SequentialFrames_RestoresStore()
    {
        AppendFrame(WAOperation.PUT, "a", [1]);
        AppendFrame(WAOperation.DELETE, "b", []);
        AppendFrame(WAOperation.PUT, "c", [2, 3, 4]);

        var store = new KeyValueStore();
        Assert.Equal(ErrorCode.None, ReadRecords(store));

        Assert.Equal(ErrorCode.None, store.TryGet("a", out var va));
        Assert.Equal([1], va);
        Assert.Equal(ErrorCode.KeyWasDeleted, store.TryGet("b", out _));
        Assert.Equal(ErrorCode.None, store.TryGet("c", out var vc));
        Assert.Equal([2, 3, 4], vc);
    }

    [Fact]
    public void Read_ManyRecords_PreservesAll()
    {
        const int n = 500;
        for (int i = 0; i < n; i++)
            AppendFrame(WAOperation.PUT, $"k{i}", [(byte)i]);

        var store = new KeyValueStore();
        Assert.Equal(ErrorCode.None, ReadRecords(store));
        Assert.Equal(n, store.Count);
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(ErrorCode.None, store.TryGet($"k{i}", out var v));
            Assert.Equal((byte)i, v![0]);
        }
    }

    [Fact]
    public void Read_CorruptTail_ReturnsCorruptedEntry()
    {
        AppendFrame(WAOperation.PUT, "good", [1]);

        // corrupt one byte inside the existing frame's body
        using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.ReadWrite))
        {
            fs.Position = fs.Length / 2;
            int b = fs.ReadByte();
            fs.Position -= 1;
            fs.WriteByte((byte)(b ^ 0xFF));
        }

        Assert.Equal(ErrorCode.EntryIsCorrupted, ReadRecords(new KeyValueStore()));
    }

    [Fact]
    public void Read_GarbageGap_ReturnsCorruptedEntry()
    {
        AppendFrame(WAOperation.PUT, "a", [1]);
        AppendFrame(WAOperation.PUT, "b", [2]);

        // chop 3 bytes off the tail of the last frame -> trailing truncation
        byte[] fileBytes = File.ReadAllBytes(logPath);
        File.WriteAllBytes(logPath, fileBytes.Take(fileBytes.Length - 3).ToArray());

        Assert.Equal(ErrorCode.EntryIsCorrupted, ReadRecords(new KeyValueStore()));
    }
}
