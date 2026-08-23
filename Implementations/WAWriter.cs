using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Enums;
using kv_store.Interfaces;

namespace kv_store.Implementations
{
    public class WAWriter : IDisposable, IWriteAheadLogger
    {
        string? path;
        FileStream? loggerFile;
        BinaryWriter? binaryWriter;

        public string? Path => path;

        public Result Initialize(string LogPath = @"./data/wal_log")
        {
            var errCode = IsPathEmpty(LogPath, out bool IsEmpty);
            if (IsEmpty)
                return errCode;

            // wtf should be done if a constructor is called with invalid arguments. other than throwing exception.
            path = LogPath;
            loggerFile = new FileStream(path, FileMode.Append, FileAccess.Write); // handle exceptions
            binaryWriter = new(loggerFile);
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
            if (loggerFile == null || path == null || binaryWriter == null)
                return new Result(ErrorCode.InvalidPath);

            walRecord.GetInBytes(out byte[] bytes);
            binaryWriter.Write(bytes); // writelineasync
            // wal_logger.FlushAsync();
            loggerFile.Flush(true);
            return new Result(ErrorCode.None);
        }

        public void Dispose()
        {
            loggerFile?.Close();
        }
    }
}
