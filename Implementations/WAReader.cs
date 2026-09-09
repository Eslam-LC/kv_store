using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Enums;

namespace kv_store.Implementations
{
    public class WAReader
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

        public ErrorCode ReadRecords(out ICollection<WARecord> records)
        {
            records = [];
            if (string.IsNullOrWhiteSpace(_path))
                return ErrorCode.UnInitializedInstance;

            try
            {
                using var logFile = new FileStream(_path, FileMode.Open, FileAccess.Read);
                if (logFile.Length == 0)
                    return ErrorCode.FileIsEmpty;
                using var binaryReader = new BinaryReader(logFile);
                do
                {
                    ErrorCode errCode = WARecord.Unframe(binaryReader, out var record);
                    if (errCode != ErrorCode.None)
                        return errCode;

                    records.Add(record!.Value);
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
