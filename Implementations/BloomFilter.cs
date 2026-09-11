using System.Text;
using kv_store.EnumsAndConstants;
using static kv_store.EnumsAndConstants.ErrorCode;
using static kv_store.Implementations.HashHelper;

namespace kv_store.Implementations
{
    public static class HashHelper
    {
        const double ln2 = 0.693147181;

        // g_i(x) = h1(x) + i · h2(x)   for i = 0 … k−1

        public static ulong H1(ReadOnlySpan<byte> bytes) =>
            System.IO.Hashing.XxHash3.HashToUInt64(bytes, 0);

        public static ulong H2(ReadOnlySpan<byte> bytes) =>
            System.IO.Hashing.XxHash3.HashToUInt64(bytes, 0xdeadbeef);

        public static ulong G(ulong h1, ulong h2, int i, long m) => (h1 + (ulong)i * h2) % (ulong)m;

        public static long BestM(long n, double p)
        {
            // m = -ln(p) * n / ln(2)^2
            return (long)Math.Ceiling(-n * Math.Log(p) / (ln2 * ln2));
        }

        public static int BestK(long n, long m)
        {
            // k = m / n * ln(2)
            return (int)Math.Round((double)m / n * ln2);
        }

        public static int ByteIndex(ulong pos) => (int)pos >> 3;

        public static int BitOffset(ulong pos) => (int)pos & 7;

        public static bool Get(byte[] BitArray, ulong pos)
        {
            return (BitArray[ByteIndex(pos)] & (byte)(1 << BitOffset(pos))) != 0;
        }

        public static ErrorCode BitArrayContains(
            byte[] bitGet,
            ReadOnlySpan<byte> key,
            long m,
            int k,
            out bool may
        )
        {
            if (bitGet == null)
            {
                may = true;
                return InstanceIsNotInitialized;
            }
            var H1 = HashHelper.H1(key);
            var H2 = HashHelper.H2(key);
            for (int i = 0; i < k; i++)
            {
                if (!Get(bitGet, G(H1, H2, i, m)))
                {
                    may = false;
                    return None;
                }
            }
            may = true;
            return None;
        }
    }

    public class BloomFilter
    {
        readonly long bitSize;
        readonly int hashCount;

        public long BitSize => bitSize;
        public int HashCount => hashCount;
        readonly BitArrayManipulator bitArray;

        public BloomFilter(long estimatedItemCount, double desiredFalsePositiveRate = 0.01)
        {
            bitSize = BestM(estimatedItemCount, desiredFalsePositiveRate);
            hashCount = BestK(estimatedItemCount, bitSize);
            bitArray = new(bitSize);
        }

        public ErrorCode Add(ReadOnlySpan<byte> key)
        {
            var H1 = HashHelper.H1(key);
            var H2 = HashHelper.H2(key);
            for (int i = 0; i < hashCount; i++)
            {
                bitArray.Set(G(H1, H2, i, bitSize));
            }
            return None;
        }

        public ErrorCode Add(string key)
        {
            return Add(Encoding.UTF8.GetBytes(key));
        }

        public ErrorCode Contains(ReadOnlySpan<byte> key, out bool MayExist)
        {
            return BitArrayContains(bitArray.GetBytes, key, bitSize, hashCount, out MayExist);
        }

        public ErrorCode Contains(string key, out bool MayExist)
        {
            return Contains(Encoding.UTF8.GetBytes(key), out MayExist);
        }

        public byte[]? GetBytes => bitArray.Serialize;
    }

    public class BitArrayManipulator(long BitCapacity)
    {
        readonly byte[] BitArray = new byte[(BitCapacity + 7) / 8];

        public void Set(ulong pos)
        {
            BitArray[ByteIndex(pos)] |= (byte)(1 << BitOffset(pos));
        }

        public void Clear()
        {
            Array.Clear(BitArray);
        }

        public byte[] GetBytes => BitArray;

        public byte[] Serialize => [.. BitArray];
    }

    public class ImmutableBloomFilter(byte[] filterBytes, long bitSize, int hashCount)
    {
        public ErrorCode Contains(ReadOnlySpan<byte> key, out bool MayExist)
        {
            return BitArrayContains(filterBytes, key, bitSize, hashCount, out MayExist);
        }

        public ErrorCode Contains(string key, out bool MayExist)
        {
            return Contains(Encoding.UTF8.GetBytes(key), out MayExist);
        }
    }
}
