using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Enums;

namespace kv_store.Implementations
{
    public class SparseIndex
    {
        readonly SkipList<string, long> entries = [];

        public ImmutableSkipList<string, long> GetEntries() => new(entries);

        public ErrorCode AddEntry(string key, long offset)
        {
            if (string.IsNullOrWhiteSpace(key))
                return ErrorCode.KeyNotValid;
            try
            {
                entries.Add(key, offset);
            }
            catch
            {
                return ErrorCode.UnexpectedError;
            }
            return ErrorCode.None;
        }

        public ErrorCode WriteIndex(BinaryWriter binaryWriter)
        {
            foreach (var entry in entries)
            {
                try
                {
                    binaryWriter.Write(entry.Key);
                    binaryWriter.Write(entry.Value);
                }
                catch (ObjectDisposedException)
                {
                    return ErrorCode.UnInitializedInstance;
                }
                catch (IOException)
                {
                    return ErrorCode.IOError;
                }
                catch
                {
                    return ErrorCode.UnexpectedError;
                }
            }
            return ErrorCode.None;
        }

        public static ErrorCode ReadIndex(
            BinaryReader r,
            int IndexLength,
            out ImmutableSkipList<string, long> keyOffsetPairs
        )
        {
            /*
            decions to make:
                parsing index continue on failure?
                parsing becomes an atomic operation either it's all there or not?
            */
            SkipList<string, long> tempList = [];
            long indexEnd = r.BaseStream.Position + IndexLength;
            try
            {
                while (r.BaseStream.Position < indexEnd)
                {
                    var key = r.ReadString();
                    var offset = r.ReadInt64();
                    var success = tempList.Add(key, offset);
                    if (!success)
                        return ErrorCode.UnexpectedError;
                }
            }
            catch (ObjectDisposedException)
            {
                return ErrorCode.UnInitializedInstance;
            }
            catch
            {
                return ErrorCode.CorruptedEntry;
            }
            finally
            {
                keyOffsetPairs = new(tempList);
            }
            return ErrorCode.None;
        }
    }
}
