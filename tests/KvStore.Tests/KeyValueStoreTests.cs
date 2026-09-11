using kv_store.Enums;
using kv_store.Implementations;
using Xunit;

namespace KvStore.Tests;

public class KeyValueStoreTests
{
    static bool HasTombstone(KeyValueStore store, string key)
    {
        store.GetImmutableKVList(out var kvl);
        foreach (var kv in kvl)
            if (kv.Key == key && kv.Value is null)
                return true;
        return false;
    }

    static int EnumerateCount(KeyValueStore store)
    {
        store.GetImmutableKVList(out var kvl);
        int n = 0;
        foreach (var _ in kvl)
            n++;
        return n;
    }

    // ----- Tombstone semantics -----

    [Fact]
    public void Delete_LiveKey_ReturnsNone_HidesValue_KeepsTombstoneNode()
    {
        var store = new KeyValueStore();
        Assert.Equal(ErrorCode.None, store.Put("k", [1]));

        Assert.Equal(ErrorCode.None, store.Delete("k"));
        Assert.Equal(ErrorCode.KeyWasDeleted, store.TryGet("k", out _));

        // tombstone node survives in memory so a flush emits a DELETE frame
        Assert.True(HasTombstone(store, "k"));
    }

    [Fact]
    public void Delete_AbsentKey_ReturnsNone_AndRecordsTombstone()
    {
        var store = new KeyValueStore();
        Assert.Equal(ErrorCode.None, store.Delete("k"));

        // tombstone recorded so an SSTable-resident copy of "k" gets deleted on flush
        Assert.True(HasTombstone(store, "k"));
    }

    [Fact]
    public void Delete_AlreadyDeletedKey_ReturnsNone_AndKeepsTombstone()
    {
        var store = new KeyValueStore();
        Assert.Equal(ErrorCode.None, store.Put("k", [1]));
        Assert.Equal(ErrorCode.None, store.Delete("k"));
        Assert.Equal(ErrorCode.None, store.Delete("k"));
        Assert.True(HasTombstone(store, "k"));
    }

    [Fact]
    public void Put_After_Delete_RevivesValue()
    {
        var store = new KeyValueStore();
        Assert.Equal(ErrorCode.None, store.Put("k", [1]));
        Assert.Equal(ErrorCode.None, store.Delete("k"));
        Assert.Equal(ErrorCode.None, store.Put("k", [2, 3]));

        Assert.Equal(ErrorCode.None, store.TryGet("k", out var value));
        Assert.Equal([2, 3], value);
        Assert.False(HasTombstone(store, "k"));
        Assert.Equal(1, EnumerateCount(store));
    }

    [Fact]
    public void Delete_LiveKey_CountsKeyBytesOnce_ForTombstone()
    {
        var store = new KeyValueStore();
        Assert.Equal(ErrorCode.None, store.Put("k", [1, 2, 3]));
        Assert.Equal(4, store.MemoryStorage); // key.Length + value.Length

        Assert.Equal(ErrorCode.None, store.Delete("k"));
        // tombstone node still holds the key, so only the value bytes drop out
        Assert.Equal(1, store.MemoryStorage);
    }

    [Fact]
    public void Delete_AbsentKey_CountsKeyBytes_ForTombstone()
    {
        var store = new KeyValueStore();
        Assert.Equal(ErrorCode.None, store.Delete("k"));

        Assert.Equal(1, store.MemoryStorage);
        Assert.Equal(ErrorCode.None, store.Delete("k")); // idempotent, no double count
        Assert.Equal(1, store.MemoryStorage);
    }

    [Fact]
    public void Count_IncludesTombstoneNodes()
    {
        var store = new KeyValueStore();
        Assert.Equal(ErrorCode.None, store.Put("a", [1]));
        Assert.Equal(ErrorCode.None, store.Put("b", [2]));
        Assert.Equal(ErrorCode.None, store.Put("c", [3]));
        Assert.Equal(ErrorCode.None, store.Delete("b"));

        Assert.Equal(3, store.Count);
        Assert.Equal(3, EnumerateCount(store));
    }

    [Fact]
    public void Clear_ResetsMemoryStorage()
    {
        var store = new KeyValueStore();
        Assert.Equal(ErrorCode.None, store.Put("a", new byte[10]));
        Assert.Equal(ErrorCode.None, store.Put("b", new byte[20]));

        Assert.Equal(ErrorCode.None, store.Clear());
        Assert.Equal(0, store.Count);
        Assert.Equal(0, store.MemoryStorage);
    }

    [Fact]
    public void Clear_ThenPut_ReaccountsFromScratch()
    {
        var store = new KeyValueStore();
        Assert.Equal(ErrorCode.None, store.Put("a", new byte[10]));
        Assert.Equal(ErrorCode.None, store.Clear());

        // re-inserting the same key must count exactly key + value once
        Assert.Equal(ErrorCode.None, store.Put("a", new byte[10]));
        Assert.Equal(11, store.MemoryStorage);
    }
}
