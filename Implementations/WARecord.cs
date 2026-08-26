using System.IO.Hashing;
using System.Text;
using kv_store.Enums;
using kv_store.Interfaces;

namespace kv_store.Implementations
{
    public record struct WARecord : IWriteAheadRecord
    {
        byte[] _Crc32Hash;
        Int32 _RecordLength;
        WAOperation _Op;
        short _KeyLength;
        byte[] _Key;
        Int32 _ValueLength;
        byte[]? _Value;
        public readonly byte[] Crc32Hash => _Crc32Hash;

        public readonly int RecordLength => _RecordLength;

        public readonly WAOperation Op => _Op;

        public readonly short KeyLength => _KeyLength;

        public readonly byte[] Key => _Key;

        public readonly int ValueLength => _ValueLength;

        public readonly byte[]? Value => _Value;

        public static Result GetRecord(
            out WARecord record,
            in WAOperation operation,
            in string key,
            in byte[]? value = null
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

        public static Result GetInBytes(in WARecord record, out byte[] bytes)
        {
            // Force Big Endian alignness
            //
            if (record._Key == null || record._Value == null)
            {
                bytes = [];
                return new Result(ErrorCode.ValueNotValid);
            }
            bytes =
            [
                .. BitConverter.GetBytes(record._RecordLength),
                (byte)record._Op,
                .. BitConverter.GetBytes(record._KeyLength),
                .. record._Key,
                .. BitConverter.GetBytes(record._ValueLength),
                .. record._Value,
            ];
            return new Result(ErrorCode.None);
        }

        public static Result GetFromBytes(in byte[] bytes, out WARecord record)
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

            // byte[] bytesToBeChecked = [.. BitConverter.GetBytes(record.RecordLength), .. bytes];
            var errCode = GetCrc32Hash(bytes, out var Checksum);
            if (errCode.Error != ErrorCode.None)
                return errCode;

            record._Crc32Hash = Checksum;
            return new Result(ErrorCode.None);
        }

        public static Result GetOpKeyValue(
            in WARecord record,
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

        public static Result GetCrc32Hash(in byte[] sourceRecordInBytes, out byte[] destination)
        {
            destination = new byte[4];
            var valid = Crc32.TryHash(sourceRecordInBytes, destination, out int _);
            if (!valid)
                return new Result(ErrorCode.UnExpectedHashingFailure);
            return new Result(ErrorCode.None);
        }

        public static Result GetInBytesWithHash(in WARecord record, out byte[] bytesWithHash)
        {
            bytesWithHash = [];
            var errCode = GetInBytes(record, out var bytes);
            if (errCode.Error != ErrorCode.None)
                return errCode;

            errCode = GetCrc32Hash(bytes.AsSpan(4).ToArray(), out var destination);
            if (errCode.Error != ErrorCode.None)
                return errCode;
            bytesWithHash = [.. destination, .. bytes];
            return new Result(ErrorCode.None);
        }
    }
}
