using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Enums;
using kv_store.Interfaces;

namespace kv_store.Implementations
{
    class KeyValueStore : IKeyValueStore
    {
        readonly Dictionary<string, byte[]> kvStore = [];

        public Result Put(string key, byte[] value)
        {
            if (key == null)
                return new Result(ErrorCode.KeyNotValid);
            if (value == null)
                return new Result(ErrorCode.ValueNotValid);

            if (kvStore.ContainsKey(key))
                kvStore[key] = value;
            else
                kvStore.TryAdd(key, value);
            return new Result(ErrorCode.None);
        }

        public Result TryGet(string key, out byte[] value)
        {
            if (key == null)
            {
                value = [];
                return new Result(ErrorCode.KeyNotValid);
            }

            var success = kvStore.TryGetValue(key, out value!);

            if (success)
                return new Result(ErrorCode.None);
            else
                return new Result(ErrorCode.KeyNotValid);
        }

        public Result Delete(string key)
        {
            if (key == null)
            {
                return new Result(ErrorCode.KeyNotValid);
            }

            var success = kvStore.Remove(key);

            if (success)
                return new Result(ErrorCode.None);
            else
                return new Result(ErrorCode.KeyNotValid);
        }

        public Result GetReadOnly(out ReadOnlyDictionary<string, byte[]> keyValuePairs)
        {
            keyValuePairs = kvStore.AsReadOnly();
            return new Result(ErrorCode.None);
        }

        public Result Clear()
        {
            kvStore.Clear();
            return new Result(ErrorCode.None);
        }
    }
}
