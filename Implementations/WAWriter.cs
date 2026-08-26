using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Enums;
using kv_store.Interfaces;

namespace kv_store.Implementations
{
    public class WAWriter : IWriteAheadLogger
    {
        string? path;

        // FileStream? loggerFile;
        // BinaryWriter? binaryWriter;

        public string? Path => path;

        public Result Initialize(string LogPath = @"./data/wal_log")
        {
            var errCode = IsPathEmpty(LogPath, out bool IsEmpty);
            if (IsEmpty)
                return errCode;

            path = LogPath;
            return new Result(ErrorCode.None);
        }

        private static Result IsPathEmpty(string _path, out bool result)
        {
            result = string.IsNullOrWhiteSpace(_path);
            if (!result)
                return new Result(ErrorCode.None);
            else
                return new Result(ErrorCode.InvalidPath);
        }

        public Result Append(WARecord walRecord)
        {
            if (string.IsNullOrWhiteSpace(path))
                return new Result(ErrorCode.InvalidPath);

            var errCode = WARecord.GetInBytesWithHash(walRecord, out byte[] bytes);
            if (errCode.Error != ErrorCode.None)
                return errCode;

            try
            {
                using var loggerFile = new FileStream(path, FileMode.Append, FileAccess.Write);
                using var binaryWriter = new BinaryWriter(loggerFile);
                binaryWriter.Write(bytes); // writelineasync
                // wal_logger.FlushAsync();
                loggerFile.Flush(true);
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

        public Result Truncate()
        {
            if (string.IsNullOrWhiteSpace(path))
                return new Result(ErrorCode.InvalidPath);

            try
            {
                using var fileLogger = new FileStream(path, FileMode.Create, FileAccess.Write);
            }
            catch (FileNotFoundException)
            {
                return new Result(ErrorCode.InvalidPath);
            }
            catch (IOException)
            {
                return new Result(ErrorCode.IOError);
            }
            catch (Exception)
            {
                return new Result(ErrorCode.UnexpectedError);
            }

            return new Result(ErrorCode.None);
        }
    }
}
