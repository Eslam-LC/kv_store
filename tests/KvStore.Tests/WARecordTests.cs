using System.Text;
using kv_store.Enums;
using kv_store.Implementations;
using Xunit;

namespace KvStore.Tests;

public class WARecordTests
{
    static WARecord MakePut(string key, byte[] value) =>
        AssertRecord(WARecord.GetRecord(out var rec, WAOperation.PUT, key, value), rec);

    static WARecord MakeDelete(string key) =>
        AssertRecord(WARecord.GetRecord(out var rec, WAOperation.DELETE, key, null), rec);

    static WARecord AssertRecord(ErrorCode code, WARecord rec)
    {
        Assert.Equal(ErrorCode.None, code);
        return rec;
    }

    [Fact]
    public void GetRecord_Put_StoresKeyAndValue()
    {
        var rec = MakePut("alpha", [1, 2, 3]);
        Assert.Equal(WAOperation.PUT, rec.Op);
        Assert.Equal("alpha", rec.KeyAsString);
        Assert.Equal(3, rec.ValueLength);
        Assert.Equal([1, 2, 3], rec.Value);
    }

    [Fact]
    public void GetRecord_Delete_OntainsNoValue()
    {
        var rec = MakeDelete("gone");
        Assert.Equal(WAOperation.DELETE, rec.Op);
        Assert.Equal("gone", rec.KeyAsString);
        Assert.Equal(0, rec.ValueLength);
        Assert.Empty(rec.Value);
    }

    [Fact]
    public void GetRecord_Put_WithNullValue_ReturnsValueNotValid()
    {
        Assert.Equal(
            ErrorCode.ValueNotValid,
            WARecord.GetRecord(out _, WAOperation.PUT, "key", null)
        );
    }

    static void AssertRecordFields(WARecord expected, WARecord? actual)
    {
        Assert.NotNull(actual);
        var a = actual.Value;
        Assert.Equal(expected.Op, a.Op);
        Assert.Equal(expected.KeyAsString, a.KeyAsString);
        Assert.Equal(expected.ValueLength, a.ValueLength);
        Assert.Equal(expected.Value, a.Value);
    }

    [Fact]
    public void GetInBytes_GetFromBytes_RoundTrip_Put()
    {
        var rec = MakePut("k1", "hello world"u8.ToArray());
        Assert.Equal(ErrorCode.None, WARecord.GetInBytes(rec, out var bytes));
        Assert.NotNull(bytes);
        Assert.Equal(ErrorCode.None, WARecord.GetFromBytes(bytes!, out var parsed));
        AssertRecordFields(rec, parsed);
    }

    [Fact]
    public void GetInBytes_GetFromBytes_RoundTrip_Delete()
    {
        var rec = MakeDelete("del");
        Assert.Equal(ErrorCode.None, WARecord.GetInBytes(rec, out var bytes));
        Assert.Equal(ErrorCode.None, WARecord.GetFromBytes(bytes!, out var parsed));
        AssertRecordFields(rec, parsed);
    }

    [Fact]
    public void GetInBytes_EmptyKey_ReturnsKeyNotValid()
    {
        Assert.Equal(ErrorCode.KeyNotValid, WARecord.GetInBytes(MakePut("  ", [1]), out _));
    }

    // ----- Frame / Unframe (the WAL envelope) -----

    [Fact]
    public void Frame_Unframe_RoundTrip_ViaBinaryReader()
    {
        var rec = MakePut("f\"ramed\"", Encoding.UTF8.GetBytes("→ β δ"));
        Assert.Equal(ErrorCode.None, WARecord.Frame(rec, out var framed));
        Assert.NotNull(framed);

        using var ms = new MemoryStream(framed);
        using var br = new BinaryReader(ms);
        Assert.Equal(ErrorCode.None, WARecord.Unframe(br, out var parsed));
        AssertRecordFields(rec, parsed);
    }

    [Fact]
    public void Frame_PutsCrcAndLengthPrefix_BeforeBody()
    {
        var rec = MakePut("x", [9]);
        Assert.Equal(ErrorCode.None, WARecord.Frame(rec, out var framed));
        Assert.Equal(ErrorCode.None, WARecord.GetInBytes(rec, out var body));

        Assert.True(framed!.Length == 8 + body!.Length, "expect [crc 4][len 4][body]");
        int declaredLen = BitConverter.ToInt32(framed, 4);
        Assert.Equal(body.Length, declaredLen);
        // body starts at offset 8
        Assert.Equal(body, framed![8..]);
    }

    [Fact]
    public void Unframe_CorruptedBody_ReturnsCorruptedEntry()
    {
        var rec = MakePut("clean", "value"u8.ToArray());
        Assert.Equal(ErrorCode.None, WARecord.Frame(rec, out var framed));
        // corrupt the first body byte (offset 8)
        framed![8] ^= 0xFF;

        using var ms = new MemoryStream(framed);
        using var br = new BinaryReader(ms);
        Assert.Equal(ErrorCode.CorruptedEntry, WARecord.Unframe(br, out _));
    }

    [Fact]
    public void Unframe_TruncatedFrame_ReturnsCorruptedEntry()
    {
        var rec = MakePut("big", "x"u8.ToArray());
        Assert.Equal(ErrorCode.None, WARecord.Frame(rec, out var framed));
        using var shortMs = new MemoryStream(framed!, 0, framed!.Length - 2);
        using var br = new BinaryReader(shortMs);
        // Position+declaredLen > stream.Len -> CorruptedEntry
        Assert.Equal(ErrorCode.CorruptedEntry, WARecord.Unframe(br, out _));
    }

    // ----- KVPairIO ---- (WARecord-independent but I/O-adjacent; keep blob beside record tests)

    [Fact]
    public void KVPairIO_WriteRead_RoundTrip()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            Assert.Equal(ErrorCode.None, KVPairIO.WritePair(w, "kk", "vv"u8.ToArray()));

        ms.Position = 0;
        using var r = new BinaryReader(ms);
        Assert.Equal(ErrorCode.None, KVPairIO.ReadPair(r, out var key, out var value));
        Assert.Equal("kk", key);
        Assert.Equal("vv"u8.ToArray(), value);
    }

    [Fact]
    public void KVPairIO_WriteRead_RoundTrip_BinaryValue()
    {
        byte[] bin = [0, 1, 2, 254, 255, 3];
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            Assert.Equal(ErrorCode.None, KVPairIO.WritePair(w, "bin", bin));

        ms.Position = 0;
        using var r = new BinaryReader(ms);
        Assert.Equal(ErrorCode.None, KVPairIO.ReadPair(r, out var key, out var value));
        Assert.Equal("bin", key);
        Assert.Equal(bin, value);
    }

    [Fact]
    public void KVPairIO_Read_OnEmptyStream_ReturnsCorruptedEntry()
    {
        using var ms = new MemoryStream();
        using var r = new BinaryReader(ms);
        Assert.Equal(ErrorCode.CorruptedEntry, KVPairIO.ReadPair(r, out _, out _));
    }

    [Fact]
    public void KVPairIO_Read_TruncatedValue_ReturnsCorruptedEntry()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write("key");
            w.Write(1000); // claims 1000 bytes, writes none
        }
        ms.Position = 0;
        using var r = new BinaryReader(ms);
        Assert.Equal(ErrorCode.CorruptedEntry, KVPairIO.ReadPair(r, out _, out _));
    }
}
