using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Enums;
using static kv_store.Implementations.BitArrayManipulator;

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

        public static ulong G(ReadOnlySpan<byte> bytes, int i, long m) =>
            (H1(bytes) + (ulong)i * H2(bytes)) % (ulong)m;

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
    }

    public class BloomFilter
    {
        readonly long bitSize;
        readonly int hashCount;
        readonly BitArrayManipulator? bitArray;

        public BloomFilter(int estimatedItemCount, double desiredFalsePositiveRate = 0.01)
        {
            bitSize = HashHelper.BestM(estimatedItemCount, desiredFalsePositiveRate);
            hashCount = HashHelper.BestK(estimatedItemCount, bitSize);
            bitArray = new(bitSize);
        }

        public ErrorCode Add(ReadOnlySpan<byte> key)
        {
            if (bitArray == null)
                return ErrorCode.UnInitializedInstance;

            for (int i = 0; i < hashCount; i++)
            {
                bitArray.Set(HashHelper.G(key, i, bitSize));
            }
            return ErrorCode.None;
        }

        public ErrorCode Contains(ReadOnlySpan<byte> key, out bool MayExist)
        {
            if (bitArray == null)
            {
                MayExist = true;
                return ErrorCode.UnInitializedInstance;
            }

            for (int i = 0; i < hashCount; i++)
            {
                if (!bitArray.Get(HashHelper.G(key, i, bitSize)))
                {
                    MayExist = false;
                    return ErrorCode.None;
                }
            }
            MayExist = true;
            return ErrorCode.None;
        }

        public byte[]? GetBytes => bitArray?.Serialize;

        public class ImmutableBloomFilter(
            ReadOnlySpan<byte> filterBytes,
            long bitSize,
            int hashCount
        )
        {
            readonly ImmutableBitArray bitArray = new(filterBytes);

            public ErrorCode Contains(ReadOnlySpan<byte> key, out bool MayExist)
            {
                if (bitArray == null)
                {
                    MayExist = true;
                    return ErrorCode.UnInitializedInstance;
                }

                for (int i = 0; i < hashCount; i++)
                {
                    if (!bitArray.Get(HashHelper.G(key, i, bitSize)))
                    {
                        MayExist = false;
                        return ErrorCode.None;
                    }
                }
                MayExist = true;
                return ErrorCode.None;
            }
        }
    }

    public class BitArrayManipulator
    {
        readonly byte[] BitArray;

        public BitArrayManipulator(long BitCapacity)
        {
            BitArray = new byte[(BitCapacity + 7) / 8];
        }

        public void Set(ulong pos)
        {
            BitArray[ByteIndex(pos)] |= (byte)(1 << BitOffset(pos));
        }

        public bool Get(ulong pos)
        {
            return (BitArray[ByteIndex(pos)] & (byte)(1 << BitOffset(pos))) != 0;
        }

        static int ByteIndex(ulong pos) => (int)pos >> 3;

        static int BitOffset(ulong pos) => (int)pos & 7;

        public void Clear()
        {
            Array.Clear(BitArray);
        }

        public byte[] Serialize => [.. BitArray];

        public class ImmutableBitArray
        {
            readonly byte[] BitArray;

            public ImmutableBitArray(ReadOnlySpan<byte> bytes)
            {
                BitArray = [.. bytes];
            }

            public bool Get(ulong pos)
            {
                return (BitArray[ByteIndex(pos)] & (byte)(1 << BitOffset(pos))) != 0;
            }

            static int ByteIndex(ulong pos) => (int)pos >> 3;

            static int BitOffset(ulong pos) => (int)pos & 7;
        }
    }
}
