using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Implementations;

namespace kv_store.Interfaces
{
    public interface IWriteAheadLogger
    {
        string? Path { get; }
        public Result Append(WARecord WARecord);
        public Result Initialize(string LogPath);
        public Result Truncate();
    }
}
