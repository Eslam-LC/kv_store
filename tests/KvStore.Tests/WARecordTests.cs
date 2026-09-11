using System.Text;
using kv_store.EnumsAndConstants;
using kv_store.Implementations;
using Xunit;

namespace KvStore.Tests;

public class WARecordTests
{
    static byte[] WriteFrame(WAOperation op, string key, byte[] value)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            Assert.Equal(ErrorCode.None, WARecord.WriteFrame(w, op, key, value));
        return ms.ToArray();
    }

    [Fact]
    public void WriteFrame_ReadFrame_RoundTrip_Put()
    {
        var framed = WriteFrame(WAOperation.PUT, "alpha", "hello"u8.ToArray());
        using var ms = new MemoryStream(framed);
        using var r = new BinaryReader(ms);
        Assert.Equal(ErrorCode.None, WARecord.ReadFrame(r, out var op, out var key, out var value));
        Assert.Equal(WAOperation.PUT, op);
        Assert.Equal("alpha", key);
        Assert.Equal("hello"u8.ToArray(), value);
    }

    [Fact]
    public void WriteFrame_ReadFrame_RoundTrip_Delete()
    {
        var framed = WriteFrame(WAOperation.DELETE, "gone", []);
        using var ms = new MemoryStream(framed);
        using var r = new BinaryReader(ms);
        Assert.Equal(ErrorCode.None, WARecord.ReadFrame(r, out var op, out var key, out var value));
        Assert.Equal(WAOperation.DELETE, op);
        Assert.Equal("gone", key);
        Assert.Null(value);
    }

    [Fact]
    public void WriteFrame_EmptyValuePut_RoundTrips()
    {
        var framed = WriteFrame(WAOperation.PUT, "k", []);
        using var ms = new MemoryStream(framed);
        using var r = new BinaryReader(ms);
        Assert.Equal(ErrorCode.None, WARecord.ReadFrame(r, out var op, out var key, out var value));
        Assert.Equal(WAOperation.PUT, op);
        Assert.Equal("k", key);
        Assert.Empty(value!);
    }

    [Fact]
    public void WriteFrame_EmptyKey_ReturnsKeyNotValid()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        Assert.Equal(ErrorCode.KeyIsInvalid, WARecord.WriteFrame(w, WAOperation.PUT, "  ", [1]));
    }

    [Fact]
    public void WriteFrame_PutsCrcBeforeBody()
    {
        var rec = WriteFrame(WAOperation.PUT, "x", [9]);
        using var ms = new MemoryStream(rec);
        using var r = new BinaryReader(ms);
        var crc = r.ReadUInt32(); // first 4 bytes are the CRC
        Assert.True(crc != 0);
        // the rest is the body: op byte + key + vlen + value
        Assert.Equal((byte)WAOperation.PUT, r.ReadByte());
        Assert.Equal("x", r.ReadString());
        Assert.Equal(1, r.ReadInt32());
        Assert.Equal(9, r.ReadByte());
    }

    [Fact]
    public void ReadFrame_CorruptedBody_ReturnsCorruptedEntry()
    {
        var framed = WriteFrame(WAOperation.PUT, "clean", "value"u8.ToArray());
        framed[6] ^= 0xFF; // one byte inside the body
        using var ms = new MemoryStream(framed);
        using var r = new BinaryReader(ms);
        Assert.Equal(ErrorCode.EntryIsCorrupted, WARecord.ReadFrame(r, out _, out _, out _));
    }

    [Fact]
    public void ReadFrame_Truncated_ReturnsCorruptedEntry()
    {
        var framed = WriteFrame(WAOperation.PUT, "big", "x"u8.ToArray());
        using var ms = new MemoryStream(framed, 0, framed.Length - 2);
        using var r = new BinaryReader(ms);
        Assert.Equal(ErrorCode.EntryIsCorrupted, WARecord.ReadFrame(r, out _, out _, out _));
    }
}
