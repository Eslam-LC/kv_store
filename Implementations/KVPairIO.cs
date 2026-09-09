using System.Buffers;
using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;
using kv_store.Enums;

namespace kv_store.Implementations
{
    public class KVPairIO
    {
        public static ErrorCode WritePair(BinaryWriter w, string key, ReadOnlySpan<byte> value)
        {
            try
            {
                int KeyLength = Encoding.UTF8.GetByteCount(key);
                int TotalLength = KeyLength + 4 + value.Length;
                byte[] bytes = ArrayPool<byte>.Shared.Rent(TotalLength);
                try
                {
                    Encoding.UTF8.GetBytes(key, bytes);
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(KeyLength), value.Length);
                    value.CopyTo(bytes.AsSpan(KeyLength + 4));
                    w.Write(Crc32.HashToUInt32(bytes.AsSpan(0, TotalLength)));
                    w.Write7BitEncodedInt(KeyLength);
                    w.Write(bytes, 0, TotalLength);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(bytes);
                }
            }
            catch (IOException)
            {
                return ErrorCode.IOError;
            }
            catch (ArgumentNullException)
            {
                return ErrorCode.KeyNotValid;
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

        public static ErrorCode ReadPair(BinaryReader r, out string? key, out byte[]? value)
        {
            key = null;
            value = null;
            try
            {
                var crc = r.ReadInt32();

                var KeyLength = r.Read7BitEncodedInt();
                if (KeyLength < 0)
                    return ErrorCode.CorruptedEntry;
                var keyBytes = r.ReadBytes(KeyLength);
                if (KeyLength != keyBytes.Length)
                    return ErrorCode.CorruptedEntry;
                key = Encoding.UTF8.GetString(keyBytes);

                var ValueLength = r.ReadInt32();
                value = r.ReadBytes(ValueLength);
                if (value.Length != ValueLength)
                {
                    return ErrorCode.CorruptedEntry;
                }

                var crcCheck = new Crc32();
                crcCheck.Append(keyBytes);
                Span<byte> lenbuf = stackalloc byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(lenbuf, ValueLength);
                crcCheck.Append(lenbuf);
                crcCheck.Append(value);

                Span<byte> hashBuf = stackalloc byte[4];
                crcCheck.GetCurrentHash(hashBuf);
                if (BinaryPrimitives.ReadInt32LittleEndian(hashBuf) != crc)
                    return ErrorCode.CorruptedEntry;
            }
            catch (EndOfStreamException)
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
    }
}
