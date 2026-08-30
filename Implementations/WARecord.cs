using System.IO.Hashing;
using System.Text;
using kv_store.Enums;

namespace kv_store.Implementations
{
    public record struct WARecord
    {
        byte[] _Crc32Hash;

        // int _RecordLength;
        WAOperation _Op;
        string _Key;
        int? _ValueLength;
        byte[]? _Value;
        public readonly byte[] Crc32Hash => _Crc32Hash;

        // public readonly int RecordLength => _RecordLength;

        public readonly WAOperation Op => _Op;

        public readonly int KeyLength => Key.Length;

        public readonly byte[] Key => Encoding.UTF8.GetBytes(_Key);

        public readonly string KeyAsString => _Key;

        public readonly int ValueLength => _ValueLength ?? 0;

        public readonly byte[] Value => _Value ?? [];

        public static ErrorCode GetRecord(
            out WARecord record,
            in WAOperation operation,
            in string key,
            in byte[]? value = null
        )
        {
            if (value == null && operation == WAOperation.PUT)
            {
                record = new();
                return ErrorCode.ValueNotValid;
            }

            record = new()
            {
                _Crc32Hash = [],
                // _RecordLength = 1 + Encoding.UTF8.GetByteCount(key) + 4 + value?.Length ?? 0,
                _Op = operation,
                _Key = key,
                _ValueLength = (operation == WAOperation.PUT) ? value?.Length : null,
                _Value = (operation == WAOperation.PUT) ? value : null,
            };

            return ErrorCode.None;
        }

        public static ErrorCode GetInBytes(in WARecord record, out byte[]? bytes)
        {
            bytes = null;
            if (string.IsNullOrWhiteSpace(record._Key))
            {
                return ErrorCode.KeyNotValid;
            }
            if (
                record._Op == WAOperation.PUT
                && (record._Value == null || record._ValueLength == null)
            )
                return ErrorCode.ValueNotValid;

            try
            {
                using var memStream = new MemoryStream();
                using var binaryWriter = new BinaryWriter(memStream);

                binaryWriter.Write((byte)record.Op);
                binaryWriter.Write(record._Key);
                if (record.Op == WAOperation.PUT)
                {
                    binaryWriter.Write(record.ValueLength);
                    binaryWriter.Write(record.Value);
                }

                bytes = [.. memStream.ToArray()];
            }
            catch (FileNotFoundException)
            {
                return ErrorCode.InvalidPath;
            }
            catch (IOException)
            {
                return ErrorCode.IOError;
            }
            catch (UnauthorizedAccessException)
            {
                return ErrorCode.AccessDenied;
            }
            catch (Exception)
            {
                return ErrorCode.UnexpectedError;
            }

            return ErrorCode.None;
        }

        public static ErrorCode GetFromBytes(in byte[] bytes, out WARecord? record)
        {
            record = null;
            if (bytes == null)
                return ErrorCode.EntryIsEmpty;

            using var memStream = new MemoryStream(bytes);
            using var binaryReader = new BinaryReader(memStream);

            try
            {
                var Op = (WAOperation)binaryReader.ReadByte();

                var p = memStream.Position;
                var Key = binaryReader.ReadString();
                var KeyBytesRead = memStream.Position - p;

                if (Key.Length <= 0 || Key.Length > bytes.Length - 1 || Key.Length > int.MaxValue)
                    return ErrorCode.CorruptedEntry;

                var ValueLength = (Op == WAOperation.PUT) ? binaryReader.ReadInt32() : 0;
                if (ValueLength < 0 || ValueLength > bytes.Length - 4 - 1 - KeyBytesRead)
                    return ErrorCode.CorruptedEntry;

                var Value = (Op == WAOperation.PUT) ? binaryReader.ReadBytes(ValueLength) : null;

                var errCode = GetCrc32Hash(bytes, out var CheckSum);
                if (errCode != ErrorCode.None)
                    return errCode;

                record = new()
                {
                    _Crc32Hash = CheckSum,
                    // _RecordLength = 1 + (int)KeyBytesRead + 4 + ValueLength,
                    _Op = Op,
                    _Key = Key,
                    _ValueLength = ValueLength,
                    _Value = Value,
                };
            }
            catch (EndOfStreamException)
            {
                return ErrorCode.CorruptedEntry;
            }
            catch (FileNotFoundException)
            {
                return ErrorCode.InvalidPath;
            }
            catch (IOException)
            {
                return ErrorCode.IOError;
            }
            catch (UnauthorizedAccessException)
            {
                return ErrorCode.AccessDenied;
            }
            catch (Exception)
            {
                return ErrorCode.UnexpectedError;
            }

            return ErrorCode.None;
        }

        public static ErrorCode GetCrc32Hash(in byte[] sourceRecordInBytes, out byte[] destination)
        {
            destination = new byte[4];
            var valid = Crc32.TryHash(sourceRecordInBytes, destination, out int _);
            if (!valid)
                return ErrorCode.UnExpectedHashingFailure;
            return ErrorCode.None;
        }

        public static ErrorCode GetInBytesWithHash(in WARecord record, out byte[]? bytesWithHash)
        {
            bytesWithHash = null;
            var errCode = GetInBytes(record, out var bytes);
            if (errCode != ErrorCode.None)
                return errCode;

            if (bytes == null)
                return ErrorCode.UnexpectedError;

            var bytesLength = BitConverter.GetBytes(bytes.Length);

            errCode = GetCrc32Hash(bytes, out var CheckSum);
            if (errCode != ErrorCode.None)
                return errCode;

            bytesWithHash = [.. CheckSum, .. bytesLength, .. bytes];

            return ErrorCode.None;
        }
    }
}
