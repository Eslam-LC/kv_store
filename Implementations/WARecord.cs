using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Enums;
using kv_store.Interfaces;

namespace kv_store.Implementations
{
    public record struct WARecord : IWriteAheadRecord
    {
        int _RecordLength;
        WAOperation _Op;
        short _KeyLength;
        byte[] _Key;
        int _ValueLength;
        byte[]? _Value;

        public readonly int RecordLength => _RecordLength;

        public readonly WAOperation Op => _Op;

        public readonly short KeyLength => _KeyLength;

        public readonly byte[] Key => _Key;

        public readonly int ValueLength => _ValueLength;

        public readonly byte[]? Value => _Value;

        public static Result GetRecord(
            out WARecord record,
            WAOperation operation,
            string key,
            byte[]? value = null
        )
        {
            record = new WARecord();
            if (string.IsNullOrWhiteSpace(key) || key.Length > short.MaxValue)
                return new Result(ErrorCode.KeyNotValid);
            if (operation == WAOperation.PUT && (value == null))
                return new Result(ErrorCode.ValueNotValid);

            record._Op = operation;
            record._KeyLength = (short)key.Length;
            record._ValueLength = (operation == WAOperation.DELETE) ? 0 : value!.Length;
            record._Key = System.Text.Encoding.UTF8.GetBytes(key);
            record._Value = (operation == WAOperation.DELETE) ? [] : value;
            record._RecordLength = // RecordLength is explicitly excluded
                sizeof(WAOperation)
                + sizeof(short)
                + sizeof(int)
                + record._KeyLength
                + record._ValueLength;

            return new Result(ErrorCode.None);
        }

        public readonly Result GetInBytes(out byte[] bytes)
        {
            if (_Key == null || _Value == null)
            {
                bytes = [];
                return new Result(ErrorCode.ValueNotValid);
            }
            bytes =
            [
                .. BitConverter.GetBytes(_RecordLength),
                (byte)_Op,
                .. BitConverter.GetBytes(_KeyLength),
                .. _Key,
                .. BitConverter.GetBytes(_ValueLength),
                .. _Value,
            ];
            return new Result(ErrorCode.None);
        }
    }
}
