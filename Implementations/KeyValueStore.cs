using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Enums;

namespace kv_store.Implementations
{
    class KeyValueStore
    {
        Dictionary<string, byte[]> kvStore = [];

        public ErrorCode Put(string key, byte[] value)
        {
            if (key == null)
                return ErrorCode.KeyNotValid;
            if (value == null)
                return ErrorCode.ValueNotValid;

            kvStore[key] = value;

            return ErrorCode.None;
        }

        public ErrorCode BulkInitialize(IDictionary<string, byte[]> dict)
        {
            if (dict == null)
                return ErrorCode.InvalidArguments;
            try
            {
                kvStore = new(dict);
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

            var success = kvStore.TryGetValue(key, out value!);

            if (success)
                return ErrorCode.None;
            else
                return ErrorCode.KeyNotFound;
        }

        public ErrorCode Delete(string key)
        {
            if (key == null)
            {
                return ErrorCode.KeyNotValid;
            }

            var success = kvStore.Remove(key);

            if (success)
                return ErrorCode.None;
            else
                return ErrorCode.KeyNotValid;
        }

        public ErrorCode GetReadOnly(out ReadOnlyDictionary<string, byte[]> keyValuePairs)
        {
            keyValuePairs = kvStore.AsReadOnly();
            return ErrorCode.None;
        }

        public ErrorCode Clear()
        {
            kvStore.Clear();
            return ErrorCode.None;
        }
    }
}
