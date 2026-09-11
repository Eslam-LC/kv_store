using kv_store.EnumsAndConstants;
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
        snapPath = Path.Combine(tempDir, "Snapshot.dat");
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

    public ErrorCode SaveSnapshot(KeyValueStore store)
    {
        {
            using FileStream stream = new(snapPath, FileMode.Create, FileAccess.Write);
            using BinaryWriter writer = new(stream);
            return Snapshot.SaveSnapshot(writer, store);
        }
    }

    public ErrorCode LoadSnapshot(KeyValueStore store)
    {
        {
            using FileStream stream = new(snapPath, FileMode.Open, FileAccess.Read);
            using BinaryReader reader = new(stream);
            return Snapshot.LoadSnapshot(reader, store);
        }
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

        Assert.Equal(ErrorCode.None, SaveSnapshot(store));

        var store2 = new KeyValueStore();
        Assert.Equal(ErrorCode.None, LoadSnapshot(store2));
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

        {
            using FileStream stream = new(snapPath, FileMode.Create, FileAccess.Write);
            using BinaryWriter writer = new(stream);

            Assert.Equal(ErrorCode.None, SaveSnapshot(empty));
        }

        {
            using FileStream stream = new(snapPath, FileMode.Open, FileAccess.Read);
            using BinaryReader reader = new(stream);

            var store2 = new KeyValueStore();
            Assert.Equal(ErrorCode.None, LoadSnapshot(store2));
            Assert.Equal(0, store2.Count);
        }
    }

    [Fact]
    public void Save_MissingFile_CreatesIt()
    {
        var store = new KeyValueStore();
        Put(store, "a", [1]);

        Assert.Equal(ErrorCode.None, SaveSnapshot(store));
        Assert.True(File.Exists(snapPath));
    }

    [Fact]
    public void Load_MissingFile_ThrowsFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() => LoadSnapshot(new KeyValueStore()));
    }

    [Fact]
    public void Load_EmptyFile_ReturnsFileIsEmpty()
    {
        File.Create(snapPath).Dispose();

        Assert.Equal(ErrorCode.FileIsEmpty, LoadSnapshot(new KeyValueStore()));
    }

    [Fact]
    public void Load_CorruptedCountHeader_ReturnsCorruptedEntry()
    {
        var store = new KeyValueStore();
        Put(store, "k", [1, 2, 3]);
        File.Create(snapPath).Dispose();

        Assert.Equal(ErrorCode.None, SaveSnapshot(store));

        // count header lives in the first 4 bytes; corrupt it to a huge value,
        // forcing the loader to try to read far more pairs than exist.
        byte[] bytes = File.ReadAllBytes(snapPath);
        bytes[0] ^= 0xFF;
        bytes[1] ^= 0xFF;
        File.WriteAllBytes(snapPath, bytes);

        var store2 = new KeyValueStore();
        Assert.Equal(ErrorCode.EntryIsCorrupted, LoadSnapshot(store2));
    }

    [Fact]
    public void Load_TruncatedFile_ReturnsCorruptedEntry()
    {
        var store = new KeyValueStore();
        Put(store, "k", [1, 2, 3]);
        File.Create(snapPath).Dispose();

        Assert.Equal(ErrorCode.None, SaveSnapshot(store));

        byte[] bytes = File.ReadAllBytes(snapPath);
        File.WriteAllBytes(snapPath, bytes.Take(3).ToArray()); // chop after count header

        Assert.Equal(ErrorCode.EntryIsCorrupted, LoadSnapshot(new KeyValueStore()));
    }

    [Fact]
    public void SaveLoad_Clear_Replay_Identity()
    {
        var store = new KeyValueStore();
        Put(store, "x", [9]);
        Put(store, "y", [8]);
        File.Create(snapPath).Dispose();

        Assert.Equal(ErrorCode.None, SaveSnapshot(store));

        var store2 = new KeyValueStore();
        Assert.Equal(ErrorCode.None, LoadSnapshot(store2));
        Assert.Equal(ErrorCode.None, store2.Delete("x"));
        Assert.Equal(ErrorCode.None, SaveSnapshot(store2));
        Assert.Equal(ErrorCode.None, LoadSnapshot(new KeyValueStore()));
        Assert.NotEqual(ErrorCode.None, store2.TryGet("x", out _));
        Assert.Equal(ErrorCode.None, store2.TryGet("y", out _));
    }

    [Fact]
    public void SaveLoad_WithTombstone_CountMatchesRecords()
    {
        var store = new KeyValueStore();
        Put(store, "a", [1]);
        Put(store, "b", [2]);
        Put(store, "c", [3]);
        Assert.Equal(ErrorCode.None, store.Delete("b"));
        File.Create(snapPath).Dispose();

        Assert.Equal(ErrorCode.None, SaveSnapshot(store));

        var store2 = new KeyValueStore();
        Assert.Equal(ErrorCode.None, LoadSnapshot(store2));
        Assert.Equal(3, store2.Count); // 2 live + 1 tombstone
    }

    [Fact]
    public void SaveLoad_WithTombstone_DeletedKeyStaysGone()
    {
        var store = new KeyValueStore();
        Put(store, "a", [1]);
        Put(store, "k", [9]);
        Assert.Equal(ErrorCode.None, store.Delete("k"));
        File.Create(snapPath).Dispose();

        Assert.Equal(ErrorCode.None, SaveSnapshot(store));
        var store2 = new KeyValueStore();
        Assert.Equal(ErrorCode.None, LoadSnapshot(store2));
        System.Console.WriteLine($"From Here");
        Assert.Equal(ErrorCode.KeyWasDeleted, store2.TryGet("k", out _));
        System.Console.WriteLine($"To There");
        Assert.Equal(ErrorCode.None, store2.TryGet("a", out var va));
        Assert.Equal([1], va);
    }
}
