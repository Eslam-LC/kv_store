using System.Text.RegularExpressions;
using kv_store.EnumsAndConstants;
using static kv_store.EnumsAndConstants.Constants;
using static kv_store.EnumsAndConstants.ErrorCode;
using static kv_store.EnumsAndConstants.MapExToEr;
using static kv_store.EnumsAndConstants.WAOperation;

namespace kv_store.Implementations
{
    /*
    the Engine may write to wal and fail to write to memory store which is to be expected.
    also it will log records with invalid keys.
    */
    public partial class WAEngine(
        string FilesPath = "./data",
        string WALFileName = "wal.log",
        string SnapshotFileName = "snapshot.dat",
        string SSTableBaseName = "SSTable"
    )
    {
        KeyValueStore? MemStore;
        readonly List<KeyValueStore> Frozen_ = [];

        readonly List<ImmutableSSTable> immutableSSTables = [];

        public string WALFile { get; } = Path.Combine(FilesPath, WALFileName);
        public string SnapshotFile { get; set; } = Path.Combine(FilesPath, SnapshotFileName);
        public string SSTFileBaseName { get; } = SSTableBaseName;

        const long flushThresholdBytes = 32768;

        [GeneratedRegex("^SSTable-([0-9]{5})$")]
        private static partial Regex MyRegex();

        public ErrorCode Init(out List<(ErrorCode e, string? f)> errors)
        {
            errors = [];

            ErrorCode errorCode;

            MemStore = new();

            try
            {
                if (!Path.Exists(FilesPath))
                    Directory.CreateDirectory(FilesPath);

                if (!File.Exists(WALFile))
                    File.Create(WALFile).Dispose();

                if (!File.Exists(SnapshotFile))
                    File.Create(SnapshotFile).Dispose();

                errorCode = LoadSnapshot();
                if (errorCode != None && errorCode != FileIsEmpty)
                    Console.WriteLine($"Snapshot failed to load. Error: {errorCode}");

                errorCode = ReplayRecords();
                if (errorCode != None && errorCode != FileIsEmpty)
                    Console.WriteLine($"WALog failed to load. Error: {errorCode}");
            }
            catch (ArgumentException)
            {
                return PathIsInvalid;
            }
            catch (Exception ex)
            {
                return GetErrorCode(ex);
            }

            var SSTablesFiles = Directory
                .GetFiles(FilesPath, $"{SSTFileBaseName}-*")
                .Where(f => MyRegex().IsMatch(Path.GetFileName(f)))
                .OrderDescending();

            bool SSTablesLoadedSuccessfully = true;

            foreach (var filePath in SSTablesFiles)
            {
                try
                {
                    errorCode = SSTable.ReadFromFileToTable(filePath, out var immutableSSTable);
                    if (errorCode != None)
                    {
                        SSTablesLoadedSuccessfully = false;
                        errors.Add(new(errorCode, filePath));

                        if (
                            errorCode != FileIsCorruptedOrVersionUnsupported
                            && errorCode != InputOutputFailed
                        )
                            return errorCode;

                        File.Move(filePath, filePath + ".corrupt");
                    }
                    else if (immutableSSTable == null)
                        return UnexpectedFailure;
                    else
                        immutableSSTables.Add(immutableSSTable);
                }
                catch (Exception ex)
                    when (ex
                            is PathTooLongException
                                or DirectoryNotFoundException
                                or FileNotFoundException
                                or ArgumentException
                                or NotSupportedException
                    )
                {
                    return PathIsInvalid;
                }
                catch (UnauthorizedAccessException)
                {
                    return AccessDenied;
                }
                catch (IOException)
                {
                    return InputOutputFailed;
                }
                catch
                {
                    return UnexpectedFailure;
                }
            }
            if (SSTablesLoadedSuccessfully)
                return None;
            return SstablesFailedToLoad;
        }

        public ErrorCode Put(string key, byte[] value)
        {
            if (MemStore == null)
                return InstanceIsNotInitialized;

            ErrorCode errorCode;
            try
            {
                using FileStream stream = new(WALFile, FileMode.Append, FileAccess.Write);
                using BinaryWriter writer = new(stream);

                errorCode = WARecord.WriteFrame(writer, PUT, key, value);
                if (errorCode != None)
                    return errorCode;
                errorCode = MemStore.Put(key, value);
                if (errorCode != None)
                    return errorCode;
                if (MemStore.MemoryStorage > flushThresholdBytes)
                {
                    errorCode = FlushToSSTable();
                    if (errorCode != None)
                        return errorCode;
                }
            }
            catch
            {
                return PathIsInvalid;
            }

            return None;
        }

        public ErrorCode TryGet(string key, out byte[]? value)
        {
            if (MemStore == null)
            {
                value = null;
                return InstanceIsNotInitialized;
            }

            var errorCode = MemStore.TryGet(key, out value);

            if (errorCode == KeyWasNotFound)
            {
                for (int i = Frozen_.Count - 1; i >= 0; i--)
                {
                    KeyValueStore l = Frozen_[i];
                    errorCode = l.TryGet(key, out value);
                    if (errorCode == None)
                        break;
                    if (errorCode != KeyWasNotFound)
                        return errorCode;
                }
                foreach (var table in immutableSSTables)
                {
                    errorCode = table.TryReadEntry(key, out value!);
                    if (errorCode == KeyWasNotFound)
                        continue;

                    if (errorCode == KeyWasDeleted)
                        return KeyWasNotFound;

                    return errorCode;
                }
            }

            if (errorCode == KeyWasDeleted)
                return KeyWasNotFound;

            if (errorCode != None)
                return errorCode;

            return None;
        }

        public ErrorCode Scan(
            string startKey,
            string endKey,
            out IEnumerable<KeyValuePair<string, byte[]>> results
        )
        {
            results = [];
            if (MemStore == null)
            {
                results = [];
                return InstanceIsNotInitialized;
            }
            SkipList<string, byte[]> keyValues = [];
            ErrorCode errorCode = MemStore.Scan(startKey, endKey, out var pairs);
            if (errorCode != None)
                return errorCode;

            foreach (var item in pairs)
            {
                keyValues.AddWithoutUpdate(item.Key, item.Value ?? Deleted!);
            }

            for (int i = Frozen_.Count - 1; i >= 0; i--)
            {
                KeyValueStore l = Frozen_[i];
                errorCode = l.Scan(startKey, endKey, out pairs);
                if (errorCode != None)
                    return errorCode;

                foreach (var item in pairs)
                {
                    keyValues.AddWithoutUpdate(item.Key, item.Value ?? Deleted!);
                }
            }

            foreach (var table in immutableSSTables)
            {
                errorCode = table.Scan(startKey, endKey, out pairs);
                if (errorCode != None)
                    return errorCode;

                foreach (var item in pairs)
                {
                    var _ = keyValues.AddWithoutUpdate(item.Key, item.Value ?? Deleted!);
                }
            }

            results = keyValues.Where(kv => kv.Value != Deleted);
            return None;
        }

        public ErrorCode Delete(string key)
        {
            if (MemStore == null)
                return InstanceIsNotInitialized;

            ErrorCode errorCode;

            try
            {
                using FileStream stream = new(WALFile, FileMode.Append, FileAccess.Write);
                using BinaryWriter writer = new(stream);

                errorCode = WARecord.WriteFrame(writer, DELETE, key, null);
                if (errorCode != None)
                    return errorCode;
                errorCode = MemStore.Delete(key);
                if (errorCode != None)
                    return errorCode;
            }
            catch (IOException)
            {
                return InputOutputFailed;
            }
            catch
            {
                return PathIsInvalid;
            }

            return None;
        }

        public ErrorCode ReplayRecords()
        {
            if (MemStore == null)
                return InstanceIsNotInitialized;

            KeyValueStore store = new();
            store.BulkPut(MemStore);

            try
            {
                using FileStream stream = new(WALFile, FileMode.Open, FileAccess.Read);
                using BinaryReader reader = new(stream);

                var errorCode = WAReader.ReadRecords(reader, in store);
                if (errorCode != None)
                    return errorCode;
            }
            catch (IOException)
            {
                return InputOutputFailed;
            }
            catch
            {
                return PathIsInvalid;
            }

            MemStore = store;

            return None;
        }

        public ErrorCode SaveSnapshot()
        {
            if (MemStore == null)
                return InstanceIsNotInitialized;

            if (string.IsNullOrWhiteSpace(SnapshotFile))
                return PathIsInvalid;
            {
                using FileStream stream = new(
                    SnapshotFile + "-tmp",
                    FileMode.Create,
                    FileAccess.Write
                );
                using BinaryWriter writer = new(stream);

                var errorCode = Snapshot.SaveSnapshot(writer, MemStore);
                if (errorCode != None)
                    return errorCode;
            }
            File.Create(WALFile).Dispose();
            File.Move(SnapshotFile + "-tmp", SnapshotFile, true);

            return None;
        }

        public ErrorCode LoadSnapshot()
        {
            if (MemStore == null)
                return InstanceIsNotInitialized;

            if (string.IsNullOrWhiteSpace(SnapshotFile))
                return PathIsInvalid;

            using FileStream stream = new(SnapshotFile, FileMode.Open, FileAccess.Read);
            using BinaryReader reader = new(stream);
            var errorCode = Snapshot.LoadSnapshot(reader, MemStore);
            if (errorCode != None)
                return errorCode;
            return None;
        }

        public ErrorCode FlushToSSTable()
        {
            if (MemStore == null || FilesPath == null)
                return InstanceIsNotInitialized;

            ErrorCode errorCode;

            var old = MemStore;
            Frozen_.Add(old);
            MemStore = new();
            errorCode = old.MakeImmutable();
            if (errorCode != None)
                return errorCode;

            while (Frozen_.Count > 0)
            {
                old = Frozen_[0];
                errorCode = old.GetImmutableKVList(
                    out ImmutableSkipList<string, byte[]?> keyValuePairs
                );
                if (errorCode != None)
                    return errorCode;
                if (keyValuePairs == null)
                    return UnexpectedFailure;

                uint SerialNumber = Directory
                    .GetFiles(FilesPath, $"{SSTFileBaseName}-*")
                    .Select(f => MyRegex().Match(Path.GetFileName(f)))
                    .Where(m => m.Success)
                    .Select(m => uint.TryParse(m.Groups[1].Value, out uint n) ? n : 0)
                    .DefaultIfEmpty(0u)
                    .Max();

                try
                {
                    var tmpPath = Path.Combine(FilesPath, $"{SSTFileBaseName}-TMP");
                    var finalPath = Path.Combine(
                        FilesPath,
                        $"{SSTFileBaseName}-{++SerialNumber:D5}"
                    );

                    errorCode = SSTable.WriteTableToFile(
                        tmpPath,
                        keyValuePairs,
                        old.Count,
                        out ImmutableSSTable? ssTable
                    );
                    if (errorCode != None)
                        return errorCode;

                    if (ssTable == null)
                        return UnexpectedFailure;

                    MoveSSTable(tmpPath, finalPath, ssTable);

                    immutableSSTables.Insert(0, ssTable);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return UnexpectedFailure;
                }
                catch (ArgumentException)
                {
                    return PathIsInvalid;
                }
                catch (Exception ex)
                {
                    return GetErrorCode(ex);
                }

                if (!Frozen_.Remove(old))
                    return UnexpectedFailure;
            }

            return None;
        }

        private static void MoveSSTable(string tmpPath, string finalPath, ImmutableSSTable ssTable)
        {
            File.Move(tmpPath, finalPath);
            ssTable.FileName = finalPath;
        }
    }
}
