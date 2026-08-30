using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Enums;

namespace kv_store.Implementations
{
    public class WAWriter
    {
        string? path;
        public string? Path => path;

        public ErrorCode Initialize(string LogPath = @"./data/wal_log")
        {
            bool IsEmpty = string.IsNullOrWhiteSpace(LogPath);

            if (IsEmpty)
                return ErrorCode.InvalidPath;

            path = LogPath;
            return ErrorCode.None;
        }

        public ErrorCode Append(WARecord walRecord)
        {
            if (string.IsNullOrWhiteSpace(path))
                return ErrorCode.InvalidPath;

            var errCode = WARecord.GetInBytesWithHash(walRecord, out byte[]? bytes);
            if (errCode != ErrorCode.None)
                return errCode;

            if (bytes == null)
                return ErrorCode.UnexpectedError;

            try
            {
                using var loggerFile = new FileStream(path, FileMode.Append, FileAccess.Write);
                using var binaryWriter = new BinaryWriter(loggerFile);
                binaryWriter.Write(bytes);
                loggerFile.Flush(true);
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

        public ErrorCode Truncate()
        {
            if (string.IsNullOrWhiteSpace(path))
                return ErrorCode.InvalidPath;

            try
            {
                using var fileLogger = new FileStream(path, FileMode.Create, FileAccess.Write);
            }
            catch (FileNotFoundException)
            {
                return ErrorCode.InvalidPath;
            }
            catch (IOException)
            {
                return ErrorCode.IOError;
            }
            catch (Exception)
            {
                return ErrorCode.UnexpectedError;
            }

            return ErrorCode.None;
        }
    }
}
