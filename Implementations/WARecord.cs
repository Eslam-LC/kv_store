using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using kv_store.Enums;
using kv_store.Interfaces;

namespace kv_store.Implementations
{
    public record struct WARecord : IWriteAheadRecord
    {
        Int32 _RecordLength;
        WAOperation _Op;
        short _KeyLength;
        byte[] _Key;
        Int32 _ValueLength;
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
            // Force Big Endian alignness
            //
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

        public static Result GetFromBytes(byte[] bytes, out WARecord record)
        {
            record = new();
            if (bytes == null)
            {
                return new Result(ErrorCode.ValueNotValid);
            }
            record._RecordLength = bytes.Length;
            record._Op = (WAOperation)bytes[0];
            record._KeyLength = BitConverter.ToInt16(bytes.AsSpan(1, 2));
            record._Key = [.. bytes.AsSpan(3, record._KeyLength)];
            if (record._Op == WAOperation.DELETE)
            {
                record._ValueLength = 0;
                record._Value = [];
            }
            else
            {
                int valueIndex = 1 + 2 + record._KeyLength;
                record._ValueLength = BitConverter.ToInt32(bytes.AsSpan(valueIndex, 4));
                record._Value = [.. bytes.AsSpan(valueIndex + 4, record._ValueLength)];
            }
            return new Result(ErrorCode.None);
        }

        public static Result GetOpKeyValue(
            WARecord record,
            out WAOperation operation,
            out string key,
            out byte[] value
        )
        {
            operation = record.Op;
            key = Encoding.UTF8.GetString(record.Key);
            value = record.Value ?? [];
            return new Result(ErrorCode.None);
        }
        // static void PrintRecordsCollection(ICollection<WARecord> records)
        // {
        //     if (records == null)
        //         return;
        //     foreach (var record in records)
        //     {
        //         Console.WriteLine($"Record Length: {record.RecordLength}");
        //         Console.WriteLine($"Operation: {record.Op}");
        //         Console.WriteLine($"Key Length: {record.KeyLength}");
        //         Console.WriteLine($"Key: {Encoding.UTF8.GetString(record.Key)}");
        //         Console.WriteLine($"Value Length: {record.ValueLength}");
        //         if (record.ValueLength > 0)
        //         {
        //             Console.Write($"Value: ");
        //             foreach (var item in record.Value ?? [])
        //             {
        //                 Console.Write($" {item}");
        //             }
        //         }
        //     }
        //     Console.WriteLine();
        // }
    }
}
