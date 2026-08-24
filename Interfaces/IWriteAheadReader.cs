using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Implementations;

namespace kv_store.Interfaces
{
    public interface IWriteAheadReader
    {
        public Result ReadRecords(out ICollection<WARecord> records);
    }
}
