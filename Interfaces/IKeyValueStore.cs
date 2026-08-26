using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace kv_store.Interfaces
{
    public interface IKeyValueStore
    {
        Result Put(string key, byte[] value);
        Result TryGet(string key, out byte[] value);

        Result Delete(string key);
        public Result GetReadOnly(out ReadOnlyDictionary<string, byte[]> keyValuePairs);
        public Result Clear();
    }
}
