using kv_store.Enums;
using static kv_store.Enums.ErrorCode;

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
                return CannotWriteToImmutableInstance;
            if (key == null)
                return KeyIsInvalid;
            if (value == null)
                return ValueIsInvalid;

            var found = KVList.TryGetValue(key, out var oldValue);
            var deltaLength = oldValue == null ? value.Length : value.Length - oldValue.Length;
            if (!found)
                deltaLength += key.Length;
            memoryStorage += deltaLength;

            KVList[key] = value;

            return None;
        }

        public ErrorCode BulkInitialize(IDictionary<string, byte[]?> dict)
        {
            if (!Mutable)
                return CannotWriteToImmutableInstance;
            if (dict == null)
                return ArgumentsAreInvalid;
            try
            {
                KVList = new(dict);
                memoryStorage = dict.Sum(kvp => kvp.Key.Length + kvp.Value?.Length ?? 0);
            }
            catch
            {
                return UnexpectedFailure;
            }
            return None;
        }

        public ErrorCode TryGet(string key, out byte[]? value)
        {
            if (key == null)
            {
                value = null;
                return KeyIsInvalid;
            }

            var success = KVList.TryGetValue(key, out value);

            if (success)
                return (value != null) ? None : KeyWasDeleted;
            else
                return KeyWasNotFound;
        }

        public ErrorCode Delete(string key)
        {
            if (!Mutable)
                return CannotWriteToImmutableInstance;
            if (key == null)
                return KeyIsInvalid;

            var found = KVList.TryGetValue(key, out var oldValue);

            var deltaLength = oldValue == null ? 0 : -oldValue.Length;
            if (!found)
                deltaLength += key.Length;
            memoryStorage += deltaLength;

            KVList[key] = null;

            return None;
        }

        public ErrorCode GetImmutableKVList(out ImmutableSkipList<string, byte[]?> keyValuePairs)
        {
            keyValuePairs = new(KVList);
            return None;
        }

        public int Count => KVList.Count;

        public ErrorCode Clear()
        {
            if (!Mutable)
                return CannotWriteToImmutableInstance;
            KVList.Clear();
            memoryStorage = 0;
            return None;
        }

        public ErrorCode MakeImmutable()
        {
            if (Mutable)
            {
                Mutable = false;
                return None;
            }
            return CannotWriteToImmutableInstance;
        }
    }
}
