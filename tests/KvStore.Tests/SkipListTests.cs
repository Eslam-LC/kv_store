using kv_store.Implementations;
using Xunit;

namespace KvStore.Tests;

public class SkipListTests
{
    // ----- Basic correctness -----

    [Fact]
    public void Insert_Then_Retrieve_ReturnsValue()
    {
        var list = new SkipList<string, int>();
        Assert.True(list.Add("a", 1));
        Assert.True(list.TryGetValue("a", out var value));
        Assert.Equal(1, value);
        Assert.Single(list);
    }

    [Fact]
    public void MissingKey_ReturnsFalse_AndDefault()
    {
        var list = new SkipList<string, int>();
        Assert.False(list.TryGetValue("nope", out var value));
        Assert.Equal(default, value);
        Assert.Empty(list);
    }

    [Fact]
    public void Indexer_MissingKey_ReturnsDefault()
    {
        var list = new SkipList<string, int>(new Dictionary<string, int> { ["x"] = 7 });
        Assert.Equal(0, list["missing"]);
    }

    [Fact]
    public void Indexer_Setter_InsertsThroughPutPath()
    {
        var list = new SkipList<string, int>();
        list["a"] = 1;
        Assert.Single(list);
        Assert.Equal(1, list["a"]);
        Assert.True(list.TryGetValue("a", out var value));
        Assert.Equal(1, value);
    }

    [Fact]
    public void Insert_ManyOutOfOrder_AllRetrievable()
    {
        var list = new SkipList<int, string>();
        int[] keys = [5, 1, 9, 3, 7, 2, 8, 4, 6, 0];
        foreach (var k in keys)
            Assert.True(list.Add(k, $"v{k}"));

        Assert.Equal(keys.Length, list.Count);
        foreach (var k in keys)
            Assert.True(list.TryGetValue(k, out var v));
    }

    [Fact]
    public void Insert_NullKey_ReturnsFalse()
    {
        var list = new SkipList<string, string>();
        Assert.False(list.Add(null!, "v"));
        Assert.Empty(list);
    }

    [Fact]
    public void Insert_NullValue_IsTombstone_KeptInList()
    {
        var list = new SkipList<string, string>();
        Assert.True(list.Add("k", null!));
        Assert.Single(list);
        Assert.True(list.TryGetValue("k", out var value));
        Assert.Null(value);
        Assert.Equal("k", list.Single().Key);
    }

    // ----- Duplicate key upsert -----

    [Fact]
    public void Insert_DuplicateKey_UpdatesValue_CountUnchanged()
    {
        var list = new SkipList<string, int>();
        Assert.True(list.Add("k", 1));
        Assert.True(list.Add("k", 2));

        Assert.Single(list);
        Assert.True(list.TryGetValue("k", out var value));
        Assert.Equal(2, value);
    }

    // ----- Regression: level-0 orphan bug -----
    // Half of inserted nodes used to end up at height 0 (empty forward array),
    // leaving them never linked into the list: unreachable but still counted.

    [Fact]
    public void Regression_InsertMany_AllReachableByEnumeration()
    {
        var list = new SkipList<int, int>();
        const int n = 1000;
        for (int i = 0; i < n; i++)
            list.Add(i, i);

        // Every inserted key must appear in the level-0 walk.
        var enumerated = list.Select(kvp => kvp.Key).ToHashSet();
        Assert.Equal(n, enumerated.Count);
        for (int i = 0; i < n; i++)
            Assert.Contains(i, enumerated);
    }

    [Fact]
    public void Regression_InsertMany_CountMatchesRetrievable()
    {
        var list = new SkipList<int, int>();
        const int n = 2000;
        for (int i = 0; i < n; i++)
            list.Add(i, i);

        int retrievable = 0;
        for (int i = 0; i < n; i++)
            if (list.TryGetValue(i, out _))
                retrievable++;

        Assert.Equal(list.Count, retrievable);
    }

    // ----- Deletion -----

    [Fact]
    public void Remove_ExistingKey_RemovesIt()
    {
        var list = new SkipList<string, int>(new Dictionary<string, int> { ["a"] = 1 });
        Assert.True(list.Remove("a"));
        Assert.False(list.TryGetValue("a", out _));
        Assert.Empty(list);
    }

    [Fact]
    public void Remove_MissingKey_ReturnsFalse()
    {
        var list = new SkipList<string, int>();
        Assert.False(list.Remove("nope"));
    }

    [Fact]
    public void Remove_Then_Reinsert_Works()
    {
        var list = new SkipList<string, int>();
        list.Add("k", 1);
        Assert.True(list.Remove("k"));
        Assert.True(list.Add("k", 2));
        Assert.True(list.TryGetValue("k", out var v));
        Assert.Equal(2, v);
        Assert.Single(list);
    }

    [Fact]
    public void Remove_ManyRandom_AllGone()
    {
        var list = new SkipList<int, int>();
        for (int i = 0; i < 500; i++)
            list.Add(i, i);

        for (int i = 0; i < 500; i += 2)
            Assert.True(list.Remove(i));

        Assert.Equal(250, list.Count);
        for (int i = 0; i < 500; i += 2)
            Assert.False(list.TryGetValue(i, out _));
        for (int i = 1; i < 500; i += 2)
            Assert.True(list.TryGetValue(i, out _));
    }

    // ----- Tombstone semantics (SetDefault) -----

    [Fact]
    public void SetDefault_ExistingKey_NullsValue_KeepsNodeLinked()
    {
        var list = new SkipList<string, string>();
        list.Add("k", "v");

        Assert.True(list.SetDefault("k"));
        Assert.True(list.TryGetValue("k", out var value));
        Assert.Null(value);
        Assert.Equal(["k"], list.Select(kvp => kvp.Key));
    }

    [Fact]
    public void SetDefault_MissingKey_ReturnsFalse()
    {
        var list = new SkipList<string, string>();
        Assert.False(list.SetDefault("nope"));
    }

    [Fact]
    public void SetDefault_KeepsCountInSyncWithEnumeration()
    {
        var list = new SkipList<string, string>();
        list.Add("a", "1");
        list.Add("b", "2");
        list.Add("c", "3");
        list.SetDefault("b");

        Assert.Equal(list.Count(), list.Count);
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void Add_After_SetDefault_RevivesValue_AndKeepsCount()
    {
        var list = new SkipList<string, string>();
        list.Add("k", "v");
        list.SetDefault("k");

        Assert.True(list.Add("k", "v2"));
        Assert.Single(list);
        Assert.True(list.TryGetValue("k", out var value));
        Assert.Equal("v2", value);
    }

    // ----- Scan boundary conditions -----

    [Fact]
    public void Scan_InclusiveRange_ReturnsBoundaries()
    {
        var list = new SkipList<int, int>();
        for (int i = 0; i < 10; i++)
            list.Add(i, i);

        var result = list.Scan(3, 6).Select(kvp => kvp.Key).ToList();
        Assert.Equal([3, 4, 5, 6], result);
    }

    [Fact]
    public void Scan_MissingStartKey_ReturnsFirstGreater()
    {
        var list = new SkipList<int, int>();
        for (int i = 0; i < 10; i += 2)
            list.Add(i, i); // 0,2,4,6,8

        // startKey=1 is absent: should include 2 onward
        var result = list.Scan(1, 6).Select(kvp => kvp.Key).ToList();
        Assert.Equal([2, 4, 6], result);
    }

    [Fact]
    public void Scan_EmptyResult_WhenStartBeyondAll()
    {
        var list = new SkipList<int, int>();
        list.Add(1, 1);
        list.Add(2, 2);

        Assert.Empty(list.Scan(10, 20));
    }

    [Fact]
    public void Scan_EmptyList_ReturnsEmpty()
    {
        var list = new SkipList<int, int>();
        Assert.Empty(list.Scan(1, 5));
    }

    [Fact]
    public void Scan_EndSmallerThanStart_ReturnsEmpty()
    {
        var list = new SkipList<int, int>();
        // Both endpoints exist and are within the list; only the ordering is wrong.
        for (int i = 0; i < 10; i++)
            list.Add(i, i);

        Assert.Empty(list.Scan(7, 3));
    }

    // ----- Enumeration order -----

    [Fact]
    public void Enumeration_IsStrictlyAscending()
    {
        var list = new SkipList<int, int>();
        int[] keys = [9, 3, 7, 1, 5, 2, 8, 4, 6, 0];
        foreach (var k in keys)
            list.Add(k, k);

        var enumerated = list.Select(kvp => kvp.Key).ToList();
        Assert.Equal(enumerated.OrderBy(x => x), enumerated);
    }

    // ----- Empty list / Clear -----

    [Fact]
    public void Clear_RemovesAll()
    {
        var list = new SkipList<string, int>();
        list.Add("a", 1);
        list.Add("b", 2);

        list.Clear();

        Assert.Empty(list);
        Assert.Empty(list);
        Assert.False(list.TryGetValue("a", out _));
        Assert.False(list.TryGetValue("b", out _));
    }

    [Fact]
    public void Clear_Then_Reinsert_Works()
    {
        var list = new SkipList<string, int>();
        list.Add("a", 1);
        list.Clear();
        Assert.True(list.Add("a", 2));
        Assert.True(list.TryGetValue("a", out var v));
        Assert.Equal(2, v);
    }
}
