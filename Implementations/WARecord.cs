using System.Buffers;
using System.IO.Hashing;
using System.Text;
using kv_store.EnumsAndConstants;
using static kv_store.EnumsAndConstants.ErrorCode;
using static kv_store.EnumsAndConstants.MapExToEr;
using static kv_store.EnumsAndConstants.WAOperation;

namespace kv_store.Implementations
{
    public record struct WARecord
    {
        public static ErrorCode WriteFrame(
            BinaryWriter w,
            WAOperation op,
            string key,
            byte[]? value
        )
        {
            ErrorCode errorCode;
            if (op != PUT && op != DELETE)
                return OperationIsInvalid;
            if (string.IsNullOrWhiteSpace(key))
                return KeyIsInvalid;
            if (op == PUT && value == null)
                return ValueIsInvalid;

            errorCode = GetCrc32Hash(op, key, value, out var CheckSum);
            if (errorCode != None)
                return errorCode;

            try
            {
                w.Write(CheckSum);
                w.Write((byte)op);
                w.Write(key);
                if (op == PUT)
                {
                    if (value == null)
                        return UnexpectedFailure;
                    w.Write(value.Length);
                    w.Write(value);
                }
            }
            catch (Exception ex)
            {
                return GetErrorCode(ex);
            }

            return errorCode;
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
            ErrorCode errorCode;
            try
            {
                var crc = r.ReadUInt32();
                op = (WAOperation)r.ReadByte();
                key = r.ReadString();
                value = (op == PUT) ? r.ReadBytes(r.ReadInt32()) : null;

                if (op == null || key == null || (op == PUT && value == null))
                    return UnexpectedFailure;

                errorCode = GetCrc32Hash((WAOperation)op, key, value, out var crcHash);
                if (errorCode != None)
                    return errorCode;

                if (crcHash != crc)
                    return EntryIsCorrupted;
            }
            catch (Exception ex) when (ex is ArgumentException or EndOfStreamException)
            {
                return EntryIsCorrupted;
            }
            catch (Exception ex)
            {
                return GetErrorCode(ex);
            }
            return errorCode;
        }

        public static ErrorCode GetCrc32Hash(
            WAOperation op,
            string key,
            byte[]? value,
            out uint CrcHash
        )
        {
            CrcHash = 0;
            if (string.IsNullOrWhiteSpace(key) || (op == PUT && (value == null)))
                return ArgumentsAreInvalid;

            int bufferLength = 1 + Encoding.UTF8.GetByteCount(key) + (value?.Length ?? 0);
            var buffer = ArrayPool<byte>.Shared.Rent(bufferLength);
            try
            {
                var span = buffer.AsSpan(0, bufferLength);
                span[0] = (byte)op;
                span = span[1..];
                int keyBytes = Encoding.UTF8.GetBytes(key, span);
                span = span[keyBytes..];
                if (value != null)
                    value.CopyTo(span);
                CrcHash = Crc32.HashToUInt32(buffer.AsSpan(0, bufferLength));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
            return None;
        }
    }
}
