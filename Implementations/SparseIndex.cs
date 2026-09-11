using kv_store.Enums;
using static kv_store.Enums.ErrorCode;
using static kv_store.Enums.MapExToEr;

namespace kv_store.Implementations
{
    public class SparseIndex
    {
        readonly SkipList<string, long> entries = [];

        public ImmutableSkipList<string, long> GetEntries() => new(entries);

        public ErrorCode AddEntry(string key, long offset)
        {
            if (string.IsNullOrWhiteSpace(key))
                return KeyIsInvalid;

            if (!entries.Add(key, offset))
                return UnexpectedFailure;

            return None;
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
                catch (Exception ex)
                {
                    return GetErrorCode(ex);
                }
            }
            return None;
        }

        public static ErrorCode ReadIndex(
            BinaryReader r,
            int IndexLength,
            out ImmutableSkipList<string, long> keyOffsetPairs
        )
        {
            SkipList<string, long> tempList = [];
            var error = None;
            long indexEnd = r.BaseStream.Position + IndexLength;
            try
            {
                while (r.BaseStream.Position < indexEnd && error == None)
                {
                    if (!tempList.Add(r.ReadString(), r.ReadInt64()))
                        error = UnexpectedFailure;
                }
            }
            catch (Exception ex)
            {
                error = GetErrorCode(ex);
            }

            keyOffsetPairs = new(tempList);

            return error;
        }
    }
}
