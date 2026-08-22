using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace kv_store
{
    public record struct WALRecord
    {
        public int RecordLength { get; set; }
        public WALOperation Op { get; set; }
        public short KeyLength { get; set; }
        public byte[] Key { get; set; }
        public int ValueLength { get; set; }
        public byte[] Value { get; set; }

        public byte[] GetInBytes()
        {
            return
            [
                .. BitConverter.GetBytes(RecordLength),
                (byte)Op,
                .. BitConverter.GetBytes(KeyLength),
                .. Key,
                .. BitConverter.GetBytes(ValueLength),
                .. Value,
            ];
        }
    }
}
