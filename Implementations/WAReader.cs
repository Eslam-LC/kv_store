using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Enums;
using kv_store.Interfaces;

namespace kv_store.Implementations
{
    public class WAReader : IWriteAheadReader
    {
        string? _path;

        // FileStream? logFile;
        // BinaryReader? binaryReader;
        public string? Path => _path;

        public Result Initialize(string path = @"./data/wal_log")
        {
            var errCode = IsFileExist(path, out bool valid);
            if (!valid)
                return errCode;

            _path = path;

            return new Result(ErrorCode.None);
        }

        private static Result IsFileExist(string _path, out bool valid)
        {
            valid = false;
            bool pathInvalid = string.IsNullOrWhiteSpace(_path);
            if (pathInvalid)
                return new Result(ErrorCode.InvalidPath);

            bool fileExists = File.Exists(_path);
            if (!fileExists)
                return new Result(ErrorCode.InvalidPath);

            valid = true;
            return new Result(ErrorCode.None);
        }

        public Result ReadRecords(out ICollection<WARecord> records)
        {
            records = [];
            if (string.IsNullOrWhiteSpace(_path))
                return new Result(ErrorCode.UnInitializedInstance);
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
                        return new Result(ErrorCode.CorruptedEntry);

                    var errCode = WARecord.GetFromBytes(bytes, out WARecord record);
                    if (errCode.Error != ErrorCode.None)
                        return errCode;

                    if (!record.Crc32Hash.SequenceEqual(hash))
                        return new Result(ErrorCode.CorruptedEntry);

                    records.Add(record);
                } while (logFile.Position < logFile.Length);
            }
            catch (FileNotFoundException)
            {
                return new Result(ErrorCode.InvalidPath);
            }
            catch (IOException)
            {
                return new Result(ErrorCode.IOError);
            }
            catch (UnauthorizedAccessException)
            {
                return new Result(ErrorCode.AccessDenied);
            }
            catch (Exception)
            {
                return new Result(ErrorCode.UnexpectedError);
            }

            return new Result(ErrorCode.None);
        }
    }
}
