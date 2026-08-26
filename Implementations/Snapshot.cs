using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Enums;
using kv_store.Interfaces;

namespace kv_store.Implementations
{
    class Snapshot : ISnapshot
    {
        string? path;

        public string? Path => path;

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

        public Result SaveSnapshot(
            in IKeyValueStore store,
            string SnapshotPath = @"./data/snapshot.dat"
        )
        {
            path = SnapshotPath;
            if (store == null)
                return new Result(ErrorCode.UnInitializedInstance);

            var errCode = store.GetReadOnly(out var roDict);
            if (errCode.Error != ErrorCode.None)
                return errCode;

            try
            {
                using var snapshotFile = new FileStream(path, FileMode.Create, FileAccess.Write);
                using var binaryWriter = new BinaryWriter(snapshotFile);

                binaryWriter.Write(roDict.Count);
                foreach (var entry in roDict)
                {
                    binaryWriter.Write(entry.Key);
                    binaryWriter.Write(entry.Value.Length);
                    binaryWriter.Write(entry.Value);
                }
            }
            catch (IOException)
            {
                return new Result(Enums.ErrorCode.IOError);
            }
            catch (UnauthorizedAccessException)
            {
                return new Result(Enums.ErrorCode.AccessDenied);
            }
            catch (Exception)
            {
                return new Result(Enums.ErrorCode.UnexpectedError);
            }
            return new Result(Enums.ErrorCode.None);
        }

        public Result LoadSnapshot(
            IKeyValueStore store,
            string SnapshotPath = @"./data/snapshot.dat"
        )
        {
            var errCode = IsFileExist(SnapshotPath, out bool valid);
            if (errCode.Error != ErrorCode.None)
                return errCode;

            if (valid)
                path = SnapshotPath;
            else
                return new Result(ErrorCode.InvalidPath);

            if (store == null)
                return new Result(ErrorCode.UnInitializedInstance);

            store.Clear();

            try
            {
                using var snapshotFile = new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.Read
                );
                using var binaryReader = new BinaryReader(snapshotFile);
                int count = binaryReader.ReadInt32();
                while (count-- > 0)
                {
                    string key = binaryReader.ReadString();
                    int ValueLength = binaryReader.ReadInt32();
                    byte[] value = binaryReader.ReadBytes(ValueLength);
                    store.Put(key, value);
                }
            }
            catch (IOException)
            {
                return new Result(Enums.ErrorCode.IOError);
            }
            catch (UnauthorizedAccessException)
            {
                return new Result(Enums.ErrorCode.AccessDenied);
            }
            catch (Exception)
            {
                return new Result(Enums.ErrorCode.UnexpectedError);
            }
            return new Result(Enums.ErrorCode.None);
        }
    }
}
