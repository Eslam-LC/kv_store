using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Implementations;

namespace kv_store.Interfaces
{
    interface ISnapshot
    {
        public Result SaveSnapshot(in IKeyValueStore store, string SnapshotPath);
        public Result LoadSnapshot(IKeyValueStore store, string SnapshotPath);
    }
}
