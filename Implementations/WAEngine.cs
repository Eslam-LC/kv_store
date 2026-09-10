using System.Text.RegularExpressions;
using kv_store.Enums;
using static kv_store.Implementations.SSTable;

namespace kv_store.Implementations
{
    /*
    the Engine may write to wal and fail to write to memory store which is to be expected.
    also it will log records with invalid keys.
    */
    public partial class WAEngine(string FilesPath = "./data")
    {
        KeyValueStore? MemStore;
        readonly List<KeyValueStore> Frozen_ = [];

        readonly List<ImmutableSSTable> immutableSSTables = [];

        public string WALFile { get; set; } = Path.Combine(FilesPath, "wal_log");
        public string SnapshotFile { get; set; } = Path.Combine(FilesPath, "snapshot.dat");
        public string SSTFileBaseName { get; set; } = "SSTable";

        const long flushThresholdBytes = 32768;

        public ErrorCode Init(out List<(ErrorCode e, string? f)> errors)
        {
            errors = [];
            if (!Path.Exists(FilesPath))
            {
                try
                {
                    Directory.CreateDirectory(FilesPath);
                }
                catch
                {
                    return ErrorCode.UnexpectedError;
                }
            }

            try
            {
                if (!File.Exists(WALFile))
                    File.Create(WALFile).Dispose();
            }
            catch
            {
                return ErrorCode.InvalidPath;
            }
            try
            {
                if (!File.Exists(SnapshotFile))
                    File.Create(SnapshotFile).Dispose();
            }
            catch
            {
                return ErrorCode.InvalidPath;
            }

            ErrorCode errorCode;

            MemStore = new();

            var SSTablesFiles = Directory
                .GetFiles(FilesPath, $"{SSTFileBaseName}-*")
                .Where(f => MyRegex().IsMatch(Path.GetFileName(f)))
                .OrderDescending();

            bool SSTablesLoadedSuccessfully = true;

            foreach (var filePath in SSTablesFiles)
            {
                try
                {
                    errorCode = SSTable.ReadFileToTable(filePath, out var immutableSSTable);
                    if (errorCode != ErrorCode.None)
                    {
                        SSTablesLoadedSuccessfully = false;
                        errors.Add(new(errorCode, filePath));

                        if (
                            errorCode != ErrorCode.FileCorruptedOrUnsupportedVersion
                            && errorCode != ErrorCode.IOError
                        )
                            return errorCode;

                        File.Move(filePath, filePath + ".corrupt");
                    }
                    else if (immutableSSTable == null)
                        return ErrorCode.UnexpectedError;
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
                    return ErrorCode.InvalidPath;
                }
                catch (UnauthorizedAccessException)
                {
                    return ErrorCode.AccessDenied;
                }
                catch (IOException)
                {
                    return ErrorCode.IOError;
                }
                catch
                {
                    return ErrorCode.UnexpectedError;
                }
            }
            if (SSTablesLoadedSuccessfully)
                return ErrorCode.None;
            return ErrorCode.ErrorInSSTablesLoading;
        }

        public ErrorCode Put(string key, byte[] value)
        {
            if (MemStore == null)
                return ErrorCode.UnInitializedInstance;

            var record = new
            {
                op = WAOperation.PUT,
                key,
                val = value,
            };

            ErrorCode errorCode;
            try
            {
                using FileStream stream = new(WALFile, FileMode.Append, FileAccess.Write);
                using BinaryWriter writer = new(stream);

                errorCode = WARecord.WriteFrame(writer, record.op, record.key, record.val);
                if (errorCode != ErrorCode.None)
                    return errorCode;
                errorCode = MemStore.Put(key, value);
                if (errorCode != ErrorCode.None)
                    return errorCode;
                if (MemStore.MemoryStorage > flushThresholdBytes)
                {
                    errorCode = FlushToSSTable();
                    if (errorCode != ErrorCode.None)
                        return errorCode;
                }
            }
            catch
            {
                return ErrorCode.InvalidPath;
            }

            return ErrorCode.None;
        }

        public ErrorCode TryGet(string key, out byte[]? value)
        {
            if (MemStore == null)
            {
                value = null;
                return ErrorCode.UnInitializedInstance;
            }

            var errorCode = MemStore.TryGet(key, out value);

            if (errorCode == ErrorCode.KeyNotFound)
            {
                for (int i = Frozen_.Count - 1; i >= 0; i--)
                {
                    KeyValueStore l = Frozen_[i];
                    errorCode = l.TryGet(key, out value);
                    if (errorCode == ErrorCode.None)
                        break;
                    if (errorCode != ErrorCode.KeyNotFound)
                        return errorCode;
                }
                foreach (var table in immutableSSTables)
                {
                    errorCode = table.TryReadEntry(key, out value!);
                    if (errorCode == ErrorCode.KeyNotFound)
                        continue;

                    if (errorCode == ErrorCode.KeyDeleted)
                        return ErrorCode.KeyNotFound;

                    return errorCode;
                }
            }

            if (errorCode == ErrorCode.KeyDeleted)
                return ErrorCode.KeyNotFound;

            if (errorCode != ErrorCode.None)
                return errorCode;

            return ErrorCode.None;
        }

        public ErrorCode Delete(string key)
        {
            if (MemStore == null)
                return ErrorCode.UnInitializedInstance;

            var record = new
            {
                op = WAOperation.DELETE,
                key,
                value = Array.Empty<byte>(),
            };

            ErrorCode errorCode;

            try
            {
                using FileStream stream = new(WALFile, FileMode.Append, FileAccess.Write);
                using BinaryWriter writer = new(stream);

                errorCode = WARecord.WriteFrame(writer, record.op, record.key, record.value);
                if (errorCode != ErrorCode.None)
                    return errorCode;
                errorCode = MemStore.Delete(key);
                if (errorCode != ErrorCode.None)
                    return errorCode;
            }
            catch (IOException)
            {
                return ErrorCode.IOError;
            }
            catch
            {
                return ErrorCode.InvalidPath;
            }

            return ErrorCode.None;
        }

        public ErrorCode ReplayRecords()
        {
            if (MemStore == null)
                return ErrorCode.UnInitializedInstance;
            try
            {
                using FileStream stream = new(WALFile, FileMode.Open, FileAccess.Read);
                using BinaryReader reader = new(stream);

                var errorCode = WAReader.ReadRecords(reader, MemStore);
                if (errorCode != ErrorCode.None)
                    return errorCode;
            }
            catch (IOException)
            {
                return ErrorCode.IOError;
            }
            catch
            {
                return ErrorCode.InvalidPath;
            }

            return ErrorCode.None;
        }

        public ErrorCode SaveSnapshot()
        {
            if (MemStore == null)
                return ErrorCode.UnInitializedInstance;

            if (string.IsNullOrWhiteSpace(SnapshotFile))
                return ErrorCode.InvalidPath;

            using FileStream stream = new(SnapshotFile, FileMode.Create, FileAccess.Write);
            using BinaryWriter writer = new(stream);

            var errorCode = Snapshot.SaveSnapshot(writer, in MemStore);
            if (errorCode != ErrorCode.None)
                return errorCode;

            File.Create(WALFile).Dispose();

            return ErrorCode.None;
        }

        public ErrorCode LoadSnapshot()
        {
            if (MemStore == null)
                return ErrorCode.UnInitializedInstance;

            if (string.IsNullOrWhiteSpace(SnapshotFile))
                return ErrorCode.InvalidPath;

            using FileStream stream = new(SnapshotFile, FileMode.Open, FileAccess.Read);
            using BinaryReader reader = new(stream);
            var errorCode = Snapshot.LoadSnapshot(reader, MemStore);
            if (errorCode != ErrorCode.None)
                return errorCode;
            return ErrorCode.None;
        }

        public ErrorCode FlushToSSTable()
        {
            if (MemStore == null || FilesPath == null)
                return ErrorCode.UnInitializedInstance;

            ErrorCode errorCode;

            var old = MemStore;
            Frozen_.Add(old);
            MemStore = new();
            errorCode = old.MakeImmutable();
            if (errorCode != ErrorCode.None)
                return errorCode;

            while (Frozen_.Count > 0)
            {
                old = Frozen_[0];
                errorCode = old.GetImmutableKVList(
                    out ImmutableSkipList<string, byte[]?> keyValuePairs
                );
                if (errorCode != ErrorCode.None)
                    return errorCode;
                if (keyValuePairs == null)
                    return ErrorCode.UnexpectedError;

                uint SerialNumber = Directory
                    .GetFiles(FilesPath, $"{SSTFileBaseName}-*")
                    .Select(f =>
                        uint.TryParse(Path.GetFileName(f).Split('-')[^1], out uint n) ? n : 0
                    )
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
                    if (errorCode != ErrorCode.None)
                        return errorCode;

                    if (ssTable == null)
                        return ErrorCode.UnexpectedError;

                    MoveSSTable(tmpPath, finalPath, ref ssTable);

                    immutableSSTables.Insert(0, ssTable);
                }
                catch
                {
                    return ErrorCode.UnexpectedError;
                }

                if (!Frozen_.Remove(old))
                    return ErrorCode.UnexpectedError;
            }

            return ErrorCode.None;
        }

        private static void MoveSSTable(
            string tmpPath,
            string finalPath,
            ref ImmutableSSTable ssTable
        )
        {
            File.Move(tmpPath, finalPath);
            ssTable.FileName_ = finalPath;
        }

        [GeneratedRegex("^SSTable-[0-9]{5}$")]
        private static partial Regex MyRegex();
    }
}
