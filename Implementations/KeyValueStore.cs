using kv_store.Enums;

namespace kv_store.Implementations
{
    public class KeyValueStore
    {
        SkipList<string, byte[]> KVList = new();
        long memoryStorage;
        public long MemoryStorage => memoryStorage;
        bool Mutable = true;

        public ErrorCode Put(string key, byte[] value)
        {
            if (!Mutable)
                return ErrorCode.WriteToImmutableInstance;
            if (key == null)
                return ErrorCode.KeyNotValid;
            if (value == null)
                return ErrorCode.ValueNotValid;

            var _ = KVList.TryGetValue(key, out var old);
            var deltaLength = old == null ? key.Length + value.Length : value.Length - old.Length;
            memoryStorage += deltaLength;

            KVList[key] = value;

            return ErrorCode.None;
        }

        public ErrorCode BulkInitialize(IDictionary<string, byte[]> dict)
        {
            if (!Mutable)
                return ErrorCode.WriteToImmutableInstance;
            if (dict == null)
                return ErrorCode.InvalidArguments;
            try
            {
                KVList = new(dict);
                memoryStorage = dict.Sum(kvp => kvp.Key.Length + kvp.Value.Length);
            }
            catch
            {
                return ErrorCode.UnexpectedError;
            }
            return ErrorCode.None;
        }

        public ErrorCode TryGet(string key, out byte[] value)
        {
            if (key == null)
            {
                value = [];
                return ErrorCode.KeyNotValid;
            }

            var success = KVList.TryGetValue(key, out value!);

            if (success)
                return ErrorCode.None;
            else
                return ErrorCode.KeyNotFound;
        }

        public ErrorCode Delete(string key)
        {
            if (!Mutable)
                return ErrorCode.WriteToImmutableInstance;
            if (key == null)
            {
                return ErrorCode.KeyNotValid;
            }

            var _ = KVList.TryGetValue(key, out var old);
            var deltaLength = old == null ? 0 : -(key.Length + old.Length);

            var success = KVList.Remove(key);

            if (success)
            {
                memoryStorage += deltaLength;
                return ErrorCode.None;
            }
            else
                return ErrorCode.KeyNotFound;
        }

        public ErrorCode GetReadOnly(out IEnumerable<KeyValuePair<string, byte[]>> keyValuePairs)
        {
            keyValuePairs = KVList;
            return ErrorCode.None;
        }

        public int Count => KVList.Count;

        public ErrorCode Clear()
        {
            if (!Mutable)
                return ErrorCode.WriteToImmutableInstance;
            KVList.Clear();
            return ErrorCode.None;
        }

        public ErrorCode MakeImmutable()
        {
            if (Mutable)
            {
                Mutable = false;
                return ErrorCode.None;
            }
            return ErrorCode.WriteToImmutableInstance;
        }
    }
}
