using kv_store.Enums;

namespace kv_store.Implementations
{
    public class KeyValueStore
    {
        SkipList<string, byte[]?> KVList = new();
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

            var found = KVList.TryGetValue(key, out var oldValue);
            var deltaLength = oldValue == null ? value.Length : value.Length - oldValue.Length;
            if (!found)
                deltaLength += key.Length;
            memoryStorage += deltaLength;

            KVList[key] = value;

            return ErrorCode.None;
        }

        public ErrorCode BulkInitialize(IDictionary<string, byte[]?> dict)
        {
            if (!Mutable)
                return ErrorCode.WriteToImmutableInstance;
            if (dict == null)
                return ErrorCode.InvalidArguments;
            try
            {
                KVList = new(dict);
                memoryStorage = dict.Sum(kvp => kvp.Key.Length + kvp.Value?.Length ?? 0);
            }
            catch
            {
                return ErrorCode.UnexpectedError;
            }
            return ErrorCode.None;
        }

        public ErrorCode TryGet(string key, out byte[]? value)
        {
            if (key == null)
            {
                value = null;
                return ErrorCode.KeyNotValid;
            }

            var success = KVList.TryGetValue(key, out value);

            if (success)
                return (value != null) ? ErrorCode.None : ErrorCode.KeyDeleted;
            else
                return ErrorCode.KeyNotFound;
        }

        public ErrorCode Delete(string key)
        {
            if (!Mutable)
                return ErrorCode.WriteToImmutableInstance;
            if (key == null)
                return ErrorCode.KeyNotValid;

            var found = KVList.TryGetValue(key, out var oldValue);

            var deltaLength = oldValue == null ? 0 : -oldValue.Length;
            if (!found)
                deltaLength += key.Length;
            memoryStorage += deltaLength;

            KVList[key] = null;

            return ErrorCode.None;
        }

        public ErrorCode GetImmutableKVList(out ImmutableSkipList<string, byte[]?> keyValuePairs)
        {
            keyValuePairs = new(KVList);
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
