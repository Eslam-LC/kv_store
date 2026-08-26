using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Enums;
using kv_store.Implementations;

namespace kv_store.Interfaces
{
    public interface IWriteAheadRecord
    {
        int RecordLength { get; }
        WAOperation Op { get; }
        short KeyLength { get; }
        byte[] Key { get; }
        int ValueLength { get; }
        byte[]? Value { get; }
    }
}
