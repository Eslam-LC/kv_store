using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace kv_store
{
    public class WalWriter : IDisposable
    {
        string? path;
        FileStream? wal_logger;
        BinaryWriter? binaryWriter;

        public Result Initialize(string LogPath = @"./data/wal_log")
        {
            var errCode = IsPathEmpty(LogPath, out bool valid);
            if (!valid)
                return errCode;

            // wtf should be done if a constructor is called with invalid arguments. other than throwing exception.
            path = LogPath;
            wal_logger = new FileStream(path, FileMode.Append, FileAccess.Write); // handle exceptions
            binaryWriter = new(wal_logger);
            return new Result(ErrorCode.None);
        }

        private static Result IsPathEmpty(string _path, out bool result)
        {
            result = !string.IsNullOrWhiteSpace(_path);
            if (result)
                return new Result(ErrorCode.None);
            else
                return new Result(ErrorCode.InvalidPath);
        }

        public Result Append(WALRecord walRecord)
        {
            if (wal_logger == null || path == null || binaryWriter == null)
                return new Result(ErrorCode.InvalidPath);

            binaryWriter.Write(walRecord.GetInBytes()); // writelineasync
            wal_logger.FlushAsync();
            wal_logger.Flush(true);
            return new Result(ErrorCode.None);
        }

        public void Dispose()
        {
            wal_logger?.Close();
        }
    }
}
