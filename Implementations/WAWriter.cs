using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Enums;

namespace kv_store.Implementations
{
    public class WAWriter
    {
        string? _path = @"./data/wal_log";

        public ErrorCode Init(string LogPath)
        {
            var valid = File.Exists(LogPath);
            try
            {
                if (!valid)
                    File.Create(LogPath).Dispose();

                _path = LogPath;
            }
            catch (UnauthorizedAccessException)
            {
                return ErrorCode.AccessDenied;
            }
            catch (Exception ex)
                when (ex
                        is PathTooLongException
                            or ArgumentException
                            or NotSupportedException
                            or DirectoryNotFoundException
                )
            {
                return ErrorCode.InvalidPath;
            }
            catch (IOException)
            {
                return ErrorCode.IOError;
            }

            return ErrorCode.None;
        }

        public ErrorCode Append(WARecord walRecord)
        {
            if (string.IsNullOrWhiteSpace(_path))
                return ErrorCode.InvalidPath;

            var errCode = WARecord.Frame(walRecord, out byte[]? bytes);
            if (errCode != ErrorCode.None)
                return errCode;

            if (bytes == null)
                return ErrorCode.UnexpectedError;

            try
            {
                using var loggerFile = new FileStream(_path, FileMode.Append, FileAccess.Write);
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
            if (string.IsNullOrWhiteSpace(_path))
                return ErrorCode.InvalidPath;

            try
            {
                using var fileLogger = new FileStream(_path, FileMode.Create, FileAccess.Write);
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
