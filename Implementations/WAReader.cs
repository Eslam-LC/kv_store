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
        FileStream? logFile;
        BinaryReader? binaryReader;

        public Result Initialize(string path = @"./data/wal_log")
        {
            var errCode = IsFileExist(path, out bool valid);
            if (!valid)
                return errCode;
            logFile = new(path, FileMode.Open, FileAccess.Read);
            binaryReader = new(logFile);

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
            if (binaryReader == null || logFile == null)
                return new Result(ErrorCode.UnInitializedInstance);
            var buffer = new byte[4];
            int bytesRead;
            do
            {
                bytesRead = binaryReader.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                    break;
                int RecordLength = BitConverter.ToInt32(buffer);
                var bytes = new byte[RecordLength];
                _ = binaryReader.Read(bytes, 0, bytes.Length);
                WARecord.GetFromBytes(bytes, out WARecord record);
                records.Add(record);
            } while (logFile.Position < logFile.Length);
            return new Result(ErrorCode.None);
        }
    }
}
