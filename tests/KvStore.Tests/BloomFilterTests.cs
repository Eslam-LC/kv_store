using System.Text;
using kv_store.Enums;
using kv_store.Implementations;
using Xunit;

namespace KvStore.Tests;

public class BloomFilterTests
{
    static byte[] Key(string s) => Encoding.UTF8.GetBytes(s);

    static bool Present(BloomFilter filter, string key)
    {
        var err = filter.Contains(Key(key), out bool mayExist);
        Assert.Equal(ErrorCode.None, err);
        return mayExist;
    }

    static bool Present(ReadOnlySpan<byte> bytes, long bitSize, int hashCount, string key)
    {
        var err = new BloomFilter.ImmutableBloomFilter(bytes, bitSize, hashCount)
            .Contains(Key(key), out bool mayExist);
        Assert.Equal(ErrorCode.None, err);
        return mayExist;
    }

    [Fact]
    public void AddedKeys_AlwaysReported_AsPossiblyPresent()
    {
        var filter = new BloomFilter(1000);
        for (int i = 0; i < 1000; i++)
        {
            var err = filter.Add(Key($"key-{i}"));
            Assert.Equal(ErrorCode.None, err);
        }

        for (int i = 0; i < 1000; i++)
            Assert.True(Present(filter, $"key-{i}"));
    }

    [Fact]
    public void EmptyFilter_ReportsNothing_Present()
    {
        var filter = new BloomFilter(100);
        for (int i = 0; i < 100; i++)
            Assert.False(Present(filter, $"absent-{i}"));
    }

    [Fact]
    public void ImmutableFilter_MatchesMutable_Decisions()
    {
        const int n = 500;
        const double p = 0.01;
        var filter = new BloomFilter(n, p);
        for (int i = 0; i < n; i++)
            filter.Add(Key($"in-{i}"));

        var bytes = filter.GetBytes;
        Assert.NotNull(bytes);

        long m = HashHelper.BestM(n, p);
        int k = HashHelper.BestK(n, m);

        for (int i = 0; i < n; i++)
            Assert.True(Present(bytes!, m, k, $"in-{i}"));

        for (int i = 0; i < n; i++)
            Assert.Equal(Present(filter, $"in-{i}"),
                         Present(bytes!, m, k, $"in-{i}"));
    }

    [Fact]
    public void SerializedBytes_RoundTrip_NoFalseNegatives()
    {
        var filter = new BloomFilter(1000, 0.01);
        for (int i = 0; i < 1000; i++)
            filter.Add(Key($"k{i}"));

        var bytes = filter.GetBytes!;
        long m = HashHelper.BestM(1000, 0.01);
        int k = HashHelper.BestK(1000, m);

        for (int i = 0; i < 1000; i++)
            Assert.True(Present(bytes, m, k, $"k{i}"));
    }

    [Fact]
    public void FalsePositiveRate_WithinStatisticalSlack()
    {
        // 1% target; allow 6x slack over a 10k-probe sample (deterministic-ish with XxHash3)
        var filter = new BloomFilter(10_000, 0.01);
        for (int i = 0; i < 10_000; i++)
            filter.Add(Key($"real-{i}"));

        int falsePositives = 0;
        const int probes = 10_000;
        for (int i = 0; i < probes; i++)
            if (Present(filter, $"absent-{i}"))
                falsePositives++;

        double measured = (double)falsePositives / probes;
        Assert.True(measured < 0.06, $"measured FP rate {measured:F4} exceeds 0.06 slack");
    }
}