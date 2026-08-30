using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Enums;

namespace kv_store.Implementations
{
    class Snapshot
    {
        string? path;
        public string? Path => path;

        public ErrorCode SaveSnapshot(
            in KeyValueStore store,
            string SnapshotPath = @"./data/snapshot.dat"
        )
        {
            path = SnapshotPath;
            if (store == null)
                return ErrorCode.UnInitializedInstance;

            var errCode = store.GetReadOnly(out var roDict);
            if (errCode != ErrorCode.None)
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

        public ErrorCode LoadSnapshot(
            KeyValueStore store,
            string SnapshotPath = @"./data/snapshot.dat"
        )
        {
            bool valid = File.Exists(SnapshotPath);

            if (valid)
                path = SnapshotPath;
            else
                return ErrorCode.InvalidPath;

            if (store == null)
                return ErrorCode.UnInitializedInstance;

            try
            {
                Dictionary<string, byte[]> tempDict = [];

                using var snapshotFile = new FileStream(path, FileMode.Open, FileAccess.Read);
                using var binaryReader = new BinaryReader(snapshotFile);
                int count = binaryReader.ReadInt32();
                while (count-- > 0)
                {
                    string key = binaryReader.ReadString();
                    int ValueLength = binaryReader.ReadInt32();
                    byte[] value = binaryReader.ReadBytes(ValueLength);
                    if (tempDict.ContainsKey(key))
                    {
                        return ErrorCode.CorruptedEntry;
                    }
                    tempDict.Add(key, value);
                }

                store.BulkInitialize(tempDict);
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
