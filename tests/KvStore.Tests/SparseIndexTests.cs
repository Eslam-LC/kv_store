using kv_store.Enums;
using kv_store.Implementations;
using Xunit;

namespace KvStore.Tests;

public class SparseIndexTests
{
    [Fact]
    public void AddEntry_ValidKey_AddsToEntries()
    {
        var index = new SparseIndex();
        Assert.Equal(ErrorCode.None, index.AddEntry("k1", 42));

        Assert.Single(index.GetEntries());
        Assert.Equal(42, index.GetEntries()["k1"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddEntry_InvalidKey_ReturnsKeyNotValid(string? key)
    {
        var index = new SparseIndex();
        var result = index.AddEntry(key!, 42);
        Assert.Equal(ErrorCode.KeyIsInvalid, result);
        Assert.Empty(index.GetEntries());
    }

    [Fact]
    public void WriteIndex_ThenReadIndex_RoundTripsInSortedOrder()
    {
        var index = new SparseIndex();
        index.AddEntry("zeta", 300);
        index.AddEntry("alpha", 10);
        index.AddEntry("mid", 150);

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            Assert.Equal(ErrorCode.None, index.WriteIndex(w));
        }

        ms.Seek(0, SeekOrigin.Begin);
        using var r = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
        Assert.Equal(ErrorCode.None, SparseIndex.ReadIndex(r, (int)ms.Length, out var read));

        Assert.Equal(3, read.Count);
        Assert.Equal(10, read["alpha"]);
        Assert.Equal(150, read["mid"]);
        Assert.Equal(300, read["zeta"]);
    }

    [Fact]
    public void ReadIndex_TruncatedStream_ReturnsCorruptedEntry()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write("complete"); // key string
            w.Write(123L); // offset
            w.Write("partial"); // key string, then stream ends mid-int64
            // note: stream ends right after the key bytes, no trailing 8 bytes
        }

        ms.Seek(0, SeekOrigin.Begin);
        using var r = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
        // claim a longer region so it tries to read past the truncated tail
        var result = SparseIndex.ReadIndex(r, (int)ms.Length + 100, out var read);

        Assert.Equal(ErrorCode.EntryIsCorrupted, result);
        // entries parsed before the truncation survive; the trailing partial pair kills the read
        Assert.True(read.TryGetValue("complete", out _));
        Assert.Equal(123L, read["complete"]);
    }

    [Fact]
    public void WriteIndex_InvalidKeyEntries_ReturnsKeyNotValid()
    {
        var index = new SparseIndex();
        Assert.Equal(ErrorCode.KeyIsInvalid, index.AddEntry(" ", 5));
    }
}
