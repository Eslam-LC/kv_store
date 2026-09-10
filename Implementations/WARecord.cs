using System.IO.Hashing;
using System.Text;
using kv_store.Enums;

namespace kv_store.Implementations
{
    public record struct WARecord
    {
        public static ErrorCode WriteInBytes(
            BinaryWriter w,
            WAOperation op,
            string key,
            byte[]? value
        )
        {
            // [ 1 byte : OP ][ UTF-8 encoded 7-bit length pre-fixed key ][ 4 bytes : value length ][ value length bytes : value ]
            if (string.IsNullOrWhiteSpace(key))
            {
                return ErrorCode.KeyNotValid;
            }
            if (op == WAOperation.PUT && (value == null))
                return ErrorCode.ValueNotValid;

            try
            {
                w.Write((byte)op);
                w.Write(key);
                if (op == WAOperation.PUT)
                {
                    if (value == null)
                        return ErrorCode.UnexpectedError;
                    w.Write(value.Length);
                    w.Write(value);
                }
            }
            catch (IOException)
            {
                return ErrorCode.IOError;
            }
            catch (ObjectDisposedException)
            {
                return ErrorCode.UnInitializedInstance;
            }
            catch
            {
                return ErrorCode.UnexpectedError;
            }

            return ErrorCode.None;
        }

        public static ErrorCode WriteFrame(
            BinaryWriter w,
            WAOperation op,
            string key,
            byte[]? value
        )
        {
            ErrorCode errorCode;
            if (op != WAOperation.PUT && op != WAOperation.DELETE)
                return ErrorCode.InvalidOperation;
            if (string.IsNullOrWhiteSpace(key))
                return ErrorCode.KeyNotValid;
            if (op == WAOperation.PUT && value == null)
                return ErrorCode.ValueNotValid;

            errorCode = GetCrc32Hash(op, key, value, out var CheckSum);
            if (errorCode != ErrorCode.None)
                return errorCode;

            try
            {
                w.Write(CheckSum);
            }
            catch (IOException)
            {
                return ErrorCode.IOError;
            }
            catch (ObjectDisposedException)
            {
                return ErrorCode.UnInitializedInstance;
            }
            catch
            {
                return ErrorCode.UnexpectedError;
            }

            errorCode = WriteInBytes(w, op, key, value);
            if (errorCode != ErrorCode.None)
                return errorCode;

            return ErrorCode.None;
        }

        public static ErrorCode ReadFromBytes(
            BinaryReader r,
            out WAOperation? op,
            out string? key,
            out byte[]? value
        )
        {
            op = null;
            key = null;
            value = null;
            // [ 1 byte : OP ][ UTF-8 encoded 7-bit length pre-fixed key ][ 4 bytes : value length ][ value length bytes : value ]
            try
            {
                op = (WAOperation)r.ReadByte();
                key = r.ReadString();
                value = (op == WAOperation.PUT) ? r.ReadBytes(r.ReadInt32()) : null;
            }
            catch (Exception ex) when (ex is EndOfStreamException or ArgumentException)
            {
                return ErrorCode.CorruptedEntry;
            }
            catch (IOException)
            {
                return ErrorCode.IOError;
            }
            catch (ObjectDisposedException)
            {
                return ErrorCode.UnInitializedInstance;
            }
            catch
            {
                return ErrorCode.UnexpectedError;
            }

            return ErrorCode.None;
        }

        public static ErrorCode ReadFrame(
            BinaryReader r,
            out WAOperation? op,
            out string? key,
            out byte[]? value
        )
        {
            op = null;
            key = null;
            value = null;
            try
            {
                ErrorCode errorCode;

                var crc = r.ReadUInt32();

                errorCode = ReadFromBytes(r, out op, out key, out value);
                if (errorCode != ErrorCode.None)
                    return errorCode;

                if (op == null || key == null || (op == WAOperation.PUT && value == null))
                    return ErrorCode.UnexpectedError;

                errorCode = GetCrc32Hash((WAOperation)op, key, value, out var crcHash);
                if (errorCode != ErrorCode.None)
                    return errorCode;

                if (crcHash != crc)
                    return ErrorCode.CorruptedEntry;
            }
            catch (Exception ex) when (ex is ArgumentException or EndOfStreamException)
            {
                return ErrorCode.CorruptedEntry;
            }
            catch (ObjectDisposedException)
            {
                return ErrorCode.UnInitializedInstance;
            }
            catch (IOException)
            {
                return ErrorCode.IOError;
            }
            catch
            {
                return ErrorCode.UnexpectedError;
            }
            return ErrorCode.None;
        }

        public static ErrorCode GetCrc32Hash(
            WAOperation op,
            string key,
            byte[]? value,
            out uint CrcHash
        )
        {
            CrcHash = 0;
            if (string.IsNullOrWhiteSpace(key) || (op == WAOperation.PUT && (value == null)))
                return ErrorCode.InvalidArguments;

            CrcHash = Crc32.HashToUInt32([
                (byte)op,
                .. Encoding.UTF8.GetBytes(key),
                .. value ?? [],
            ]);
            return ErrorCode.None;
        }
    }
}
