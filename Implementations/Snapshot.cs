using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Enums;

namespace kv_store.Implementations
{
    public class Snapshot
    {
        public string SnapshotPath { get; set; } = @"./data/snapshot.dat";

        public ErrorCode SaveSnapshot(in KeyValueStore store, string? path = null)
        {
            var path_ = path ?? SnapshotPath;
            if (!Path.Exists(path_) || store == null)
                return ErrorCode.UnInitializedInstance;

            var errCode = store.GetReadOnly(out var roDict);
            if (errCode != ErrorCode.None)
                return errCode;

            try
            {
                using var snapshotFile = new FileStream(path_, FileMode.Create, FileAccess.Write);
                using var binaryWriter = new BinaryWriter(snapshotFile);

                binaryWriter.Write(store.Count); // deliberatly not using roDict.Count() to avoid unecessary O(n) traverse over the enumerable
                foreach (var entry in roDict)
                {
                    errCode = KVPairIO.WritePair(binaryWriter, entry.Key, entry.Value);
                    if (errCode != ErrorCode.None)
                        return errCode;
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

        public ErrorCode LoadSnapshot(KeyValueStore store, string? path = null)
        {
            bool valid = File.Exists(SnapshotPath);

            var path_ = path ?? SnapshotPath;
            if (!Path.Exists(path_) || store == null)
                return ErrorCode.UnInitializedInstance;

            try
            {
                Dictionary<string, byte[]> tempDict = [];

                using var snapshotFile = new FileStream(path_, FileMode.Open, FileAccess.Read);
                if (snapshotFile.Length == 0)
                    return ErrorCode.FileIsEmpty;
                using var binaryReader = new BinaryReader(snapshotFile);
                int count = binaryReader.ReadInt32();
                while (count-- > 0)
                {
                    var errCode = KVPairIO.ReadPair(
                        binaryReader,
                        out string? key,
                        out byte[]? value
                    );
                    if (errCode != ErrorCode.None)
                        return errCode;

                    if (key == null || value == null)
                    {
                        Console.WriteLine($"Here is the Bug.");
                        return ErrorCode.UnexpectedError;
                    }

                    if (tempDict.ContainsKey(key))
                        return ErrorCode.CorruptedEntry;

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
