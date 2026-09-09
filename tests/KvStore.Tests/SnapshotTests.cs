using kv_store.Enums;
using kv_store.Implementations;
using Xunit;

namespace KvStore.Tests;

public class SnapshotTests : IDisposable
{
    readonly string tempDir;
    readonly string snapPath;

    public SnapshotTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "kv-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        snapPath = Path.Combine(tempDir, "snapshot.dat");
    }

    public void Dispose()
    {
        try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
    }

    static void Put(KeyValueStore store, string key, byte[] value)
    {
        Assert.Equal(ErrorCode.None, store.Put(key, value));
    }

    [Fact]
    public void SaveLoad_RoundTrip_PreservesAllPairs()
    {
        var store = new KeyValueStore();
        Put(store, "a", [1]);
        Put(store, "b", "hello"u8.ToArray());
        Put(store, "c", [0, 0, 255, 254]);

        File.Create(snapPath).Dispose();
        var snapshot = new Snapshot();
        Assert.Equal(ErrorCode.None, snapshot.SaveSnapshot(in store, snapPath));

        var store2 = new KeyValueStore();
        Assert.Equal(ErrorCode.None, snapshot.LoadSnapshot(store2, snapPath));
        Assert.Equal(3, store2.Count);

        Assert.Equal(ErrorCode.None, store2.TryGet("a", out var va));
        Assert.Equal([1], va);
        Assert.Equal(ErrorCode.None, store2.TryGet("b", out var vb));
        Assert.Equal("hello"u8.ToArray(), vb);
        Assert.Equal(ErrorCode.None, store2.TryGet("c", out var vc));
        Assert.Equal([0, 0, 255, 254], vc);
    }

    [Fact]
    public void SaveLoad_RoundTrip_EmptyStore()
    {
        File.Create(snapPath).Dispose();
        var empty = new KeyValueStore();
        var snapshot = new Snapshot();
        Assert.Equal(ErrorCode.None, snapshot.SaveSnapshot(in empty, snapPath));

        var store2 = new KeyValueStore();
        Assert.Equal(ErrorCode.None, snapshot.LoadSnapshot(store2, snapPath));
        Assert.Equal(0, store2.Count);
    }

    [Fact]
    public void Save_MissingFile_ReturnsUnInitialized()
    {
        // SaveSnapshot guards on Path.Exists(path) — file must pre-exist
        var store = new KeyValueStore();
        Put(store, "a", [1]);
        var snapshot = new Snapshot();
        Assert.Equal(ErrorCode.UnInitializedInstance, snapshot.SaveSnapshot(in store, snapPath));
    }

    [Fact]
    public void Load_MissingFile_ReturnsUnInitialized()
    {
        var snapshot = new Snapshot();
        Assert.Equal(
            ErrorCode.UnInitializedInstance,
            snapshot.LoadSnapshot(new KeyValueStore(), snapPath)
        );
    }

    [Fact]
    public void Load_EmptyFile_ReturnsFileIsEmpty()
    {
        File.Create(snapPath).Dispose();
        var snapshot = new Snapshot();
        Assert.Equal(
            ErrorCode.FileIsEmpty,
            snapshot.LoadSnapshot(new KeyValueStore(), snapPath)
        );
    }

    [Fact]
    public void Load_CorruptedCountHeader_ReturnsCorruptedEntry()
    {
        var store = new KeyValueStore();
        Put(store, "k", [1, 2, 3]);
        File.Create(snapPath).Dispose();
        var snapshot = new Snapshot();
        Assert.Equal(ErrorCode.None, snapshot.SaveSnapshot(in store, snapPath));

        // count header lives in the first 4 bytes; corrupt it to a huge value,
        // forcing the loader to try to read far more pairs than exist.
        byte[] bytes = File.ReadAllBytes(snapPath);
        bytes[0] ^= 0xFF;
        bytes[1] ^= 0xFF;
        File.WriteAllBytes(snapPath, bytes);

        var store2 = new KeyValueStore();
        Assert.Equal(ErrorCode.CorruptedEntry, snapshot.LoadSnapshot(store2, snapPath));
    }

    [Fact]
    public void Load_TruncatedFile_ReturnsCorruptedEntry()
    {
        var store = new KeyValueStore();
        Put(store, "k", [1, 2, 3]);
        File.Create(snapPath).Dispose();
        var snapshot = new Snapshot();
        Assert.Equal(ErrorCode.None, snapshot.SaveSnapshot(in store, snapPath));

        byte[] bytes = File.ReadAllBytes(snapPath);
        File.WriteAllBytes(snapPath, bytes.Take(3).ToArray()); // chop after count header

        Assert.Equal(
            ErrorCode.CorruptedEntry,
            snapshot.LoadSnapshot(new KeyValueStore(), snapPath)
        );
    }

    [Fact]
    public void SaveLoad_Clear_Replay_Identity()
    {
        var store = new KeyValueStore();
        Put(store, "x", [9]);
        Put(store, "y", [8]);
        File.Create(snapPath).Dispose();
        var snapshot = new Snapshot();
        Assert.Equal(ErrorCode.None, snapshot.SaveSnapshot(in store, snapPath));

        var store2 = new KeyValueStore();
        Assert.Equal(ErrorCode.None, snapshot.LoadSnapshot(store2, snapPath));
        Assert.Equal(ErrorCode.None, store2.Delete("x"));
        Assert.Equal(ErrorCode.None, snapshot.SaveSnapshot(in store2, snapPath));
        Assert.Equal(
            ErrorCode.None,
            snapshot.LoadSnapshot(new KeyValueStore(), snapPath)
        );
        Assert.False(store2.TryGet("x", out _) == ErrorCode.None);
        Assert.True(store2.TryGet("y", out _) == ErrorCode.None);
    }
}