using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Implementations;

namespace kv_store.Interfaces
{
    public interface IWriteAheadEngine
    {
        public Result Put(string key, byte[] value);
        public Result TryGet(string key, out byte[] value);
        public Result Delete(string key);
        public Result ReplayRecords();
        public Result SaveSnapshot(string path);
        public Result LoadSnapshot(string path);
    }
}
