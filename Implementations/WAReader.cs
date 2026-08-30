using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Enums;

namespace kv_store.Implementations
{
    public class WAReader
    {
        string? _path;
        public string? Path => _path;

        public ErrorCode Initialize(string path = @"./data/wal_log")
        {
            var errCode = IsFileExist(path, out bool valid);
            if (!valid)
                return errCode;

            _path = path;

            return ErrorCode.None;
        }

        private static ErrorCode IsFileExist(string _path, out bool valid)
        {
            valid = false;
            bool pathInvalid = string.IsNullOrWhiteSpace(_path);
            if (pathInvalid)
                return ErrorCode.InvalidPath;

            bool fileExists = File.Exists(_path);
            if (!fileExists)
                return ErrorCode.InvalidPath;

            valid = true;
            return ErrorCode.None;
        }

        public ErrorCode ReadRecords(out ICollection<WARecord> records)
        {
            records = [];
            if (string.IsNullOrWhiteSpace(_path))
                return ErrorCode.UnInitializedInstance;
            var buffer = new byte[4];
            int bytesRead;
            try
            {
                using var logFile = new FileStream(_path, FileMode.Open, FileAccess.Read);
                using var binaryReader = new BinaryReader(logFile);
                do
                {
                    bytesRead = binaryReader.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                        break;
                    byte[] hash = [.. buffer];

                    bytesRead = binaryReader.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                        break;
                    int RecordLength = BitConverter.ToInt32(buffer);

                    var bytes = new byte[RecordLength];
                    bytesRead = binaryReader.Read(bytes, 0, bytes.Length);
                    if (bytesRead < bytes.Length)
                        return ErrorCode.CorruptedEntry;

                    var errCode = WARecord.GetFromBytes(bytes, out WARecord? _record);
                    if (errCode != ErrorCode.None)
                        return errCode;

                    WARecord record;
                    if (_record == null)
                        return ErrorCode.UnexpectedError;
                    else
                        record = (WARecord)_record;

                    if (!record.Crc32Hash.SequenceEqual(hash))
                        return ErrorCode.CorruptedEntry;

                    records.Add(record);
                } while (logFile.Position < logFile.Length);
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
    }
}
