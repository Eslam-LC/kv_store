using kv_store.Enums;
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
        try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
    }

    static WARecord Put(string key, byte[] value)
    {
        Assert.Equal(ErrorCode.None, WARecord.GetRecord(out var rec, WAOperation.PUT, key, value));
        return rec;
    }

    static WARecord Del(string key)
    {
        Assert.Equal(ErrorCode.None, WARecord.GetRecord(out var rec, WAOperation.DELETE, key, null));
        return rec;
    }

    [Fact]
    public void Init_CreatesFile()
    {
        var writer = new WAWriter();
        Assert.Equal(ErrorCode.None, writer.Init(logPath));
        Assert.True(File.Exists(logPath));
    }

    [Fact]
    public void Init_InvalidPath_ReturnsError()
    {
        var writer = new WAWriter();
        // path inside a non-existent directory -> create fails
        var bad = Path.Combine(tempDir, "nope", "wal");
        var err = writer.Init(bad);
        Assert.True(err is ErrorCode.InvalidPath or ErrorCode.IOError);
    }

    [Fact]
    public void Append_Then_Read_ReturnsSameRecordsInOrder()
    {
        var writer = new WAWriter();
        Assert.Equal(ErrorCode.None, writer.Init(logPath));
        var recs = new[] { Put("a", [1]), Del("b"), Put("c", [2, 3, 4]) };
        foreach (var r in recs)
            Assert.Equal(ErrorCode.None, writer.Append(r));

        var reader = new WAReader();
        Assert.Equal(ErrorCode.None, reader.Init(logPath));
        Assert.Equal(ErrorCode.None, reader.ReadRecords(out var read));
        Assert.Equal(3, read.Count);
        for (int i = 0; i < recs.Length; i++)
        {
            Assert.Equal(recs[i].Op, read.ElementAt(i).Op);
            Assert.Equal(recs[i].KeyAsString, read.ElementAt(i).KeyAsString);
            Assert.Equal(recs[i].ValueLength, read.ElementAt(i).ValueLength);
            Assert.Equal(recs[i].Value, read.ElementAt(i).Value);
        }
    }

    [Fact]
    public void Append_ManyRecords_PreservesAll()
    {
        var writer = new WAWriter();
        Assert.Equal(ErrorCode.None, writer.Init(logPath));

        const int n = 500;
        for (int i = 0; i < n; i++)
            Assert.Equal(ErrorCode.None, writer.Append(Put($"k{i}", [(byte)i])));

        var reader = new WAReader();
        Assert.Equal(ErrorCode.None, reader.Init(logPath));
        Assert.Equal(ErrorCode.None, reader.ReadRecords(out var read));
        Assert.Equal(n, read.Count);
        for (int i = 0; i < n; i++)
        {
            Assert.Equal($"k{i}", read.ElementAt(i).KeyAsString);
            Assert.Equal((byte)i, read.ElementAt(i).Value[0]);
        }
    }

    [Fact]
    public void Read_EmptyLog_ReturnsFileIsEmpty()
    {
        var writer = new WAWriter();
        Assert.Equal(ErrorCode.None, writer.Init(logPath));

        var reader = new WAReader();
        Assert.Equal(ErrorCode.None, reader.Init(logPath));
        Assert.Equal(ErrorCode.FileIsEmpty, reader.ReadRecords(out _));
    }

    [Fact]
    public void Truncate_EmptiesLog()
    {
        var writer = new WAWriter();
        Assert.Equal(ErrorCode.None, writer.Init(logPath));
        Assert.Equal(ErrorCode.None, writer.Append(Put("pre", [1])));

        Assert.Equal(ErrorCode.None, writer.Truncate());

        var reader = new WAReader();
        Assert.Equal(ErrorCode.None, reader.Init(logPath));
        Assert.Equal(ErrorCode.FileIsEmpty, reader.ReadRecords(out _));
    }

    [Fact]
    public void Append_AfterTruncate_ReplayWorksFresh()
    {
        var writer = new WAWriter();
        Assert.Equal(ErrorCode.None, writer.Init(logPath));
        Assert.Equal(ErrorCode.None, writer.Append(Put("old", [1])));
        Assert.Equal(ErrorCode.None, writer.Truncate());
        Assert.Equal(ErrorCode.None, writer.Append(Put("new", [2])));

        var reader = new WAReader();
        Assert.Equal(ErrorCode.None, reader.Init(logPath));
        Assert.Equal(ErrorCode.None, reader.ReadRecords(out var read));
        Assert.Single(read);
        Assert.Equal("new", read.First().KeyAsString);
    }

    [Fact]
    public void Read_CorruptTail_ReturnsCorruptedEntry()
    {
        var writer = new WAWriter();
        Assert.Equal(ErrorCode.None, writer.Init(logPath));
        Assert.Equal(ErrorCode.None, writer.Append(Put("good", [1])));

        // corrupt one byte inside the existing frame's body
        using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.ReadWrite))
        {
            Assert.True(fs.Length > 16, "frame must be 8-byte envelope + body");
            fs.Position = fs.Length / 2;
            int b = fs.ReadByte();
            fs.Position -= 1;
            fs.WriteByte((byte)(b ^ 0xFF));
        }

        var reader = new WAReader();
        Assert.Equal(ErrorCode.None, reader.Init(logPath));
        var err = reader.ReadRecords(out _);
        Assert.Equal(ErrorCode.CorruptedEntry, err);
    }

    [Fact]
    public void Read_GarbageGap_ReturnsCorruptedEntry()
    {
        var writer = new WAWriter();
        Assert.Equal(ErrorCode.None, writer.Init(logPath));
        Assert.Equal(ErrorCode.None, writer.Append(Put("a", [1])));
        Assert.Equal(ErrorCode.None, writer.Append(Put("b", [2])));

        // chop 3 bytes off the tail of the second frame -> trailing truncation
        byte[] fileBytes = File.ReadAllBytes(logPath);
        File.WriteAllBytes(logPath, fileBytes.Take(fileBytes.Length - 3).ToArray());

        var reader = new WAReader();
        Assert.Equal(ErrorCode.None, reader.Init(logPath));
        Assert.Equal(ErrorCode.CorruptedEntry, reader.ReadRecords(out _));
    }
}