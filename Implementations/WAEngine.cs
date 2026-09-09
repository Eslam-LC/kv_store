using System.Text.RegularExpressions;
using kv_store.Enums;
using static kv_store.Implementations.SSTable;

namespace kv_store.Implementations
{
    /*
    the Engine may write to wal and fail to write to memory store which is to be expected.
    also it will log records with invalid keys.
    */
    public partial class WAEngine
    {
        WAWriter? LogWriter_;
        WAReader? LogReader_;
        KeyValueStore? MemStore_;
        readonly List<KeyValueStore> Frozen_ = [];
        Snapshot? Snapshot_;

        readonly List<ImmutableSSTable> immutableSSTables = [];
        string? Path_;

        const long flushThresholdBytes = 32768;

        public ErrorCode Init(out List<(ErrorCode e, string? f)> errors, string path = "./data")
        {
            errors = [];
            if (path == null || !Path.Exists(path))
                return ErrorCode.InvalidPath;

            Path_ = path;

            LogWriter_ = new();
            var errCode = LogWriter_.Init(Path.Combine(path, "wal_log"));
            if (errCode != ErrorCode.None)
                return errCode;
            LogReader_ = new();
            errCode = LogReader_.Init(Path.Combine(path, "wal_log"));
            if (errCode != ErrorCode.None)
                return errCode;
            MemStore_ = new();
            Snapshot_ = new() { SnapshotPath = path };

            var SSTablesFiles = Directory
                .GetFiles(Path_, $"{SSTableBaseName}-*")
                .Where(f => MyRegex().IsMatch(Path.GetFileName(f)))
                .OrderDescending();

            bool SSTablesLoadedSuccessfully = true;

            foreach (var filePath in SSTablesFiles)
            {
                try
                {
                    errCode = SSTable.ReadFileToTable(filePath, out var immutableSSTable);
                    if (errCode != ErrorCode.None)
                    {
                        SSTablesLoadedSuccessfully = false;
                        errors.Add(new(errCode, filePath));

                        if (
                            errCode != ErrorCode.FileCorruptedOrUnsupportedVersion
                            && errCode != ErrorCode.IOError
                        )
                            return errCode;

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
            if (LogWriter_ == null || MemStore_ == null)
                return ErrorCode.UnInitializedInstance;

            var errCode = WARecord.GetRecord(out WARecord record, WAOperation.PUT, key, value);

            if (errCode == ErrorCode.None)
            {
                errCode = LogWriter_.Append(record);
                if (errCode != ErrorCode.None)
                    return errCode;
                errCode = MemStore_.Put(key, value);
                if (errCode != ErrorCode.None)
                    return errCode;
                if (MemStore_.MemoryStorage > flushThresholdBytes)
                {
                    errCode = FlushToSSTable();
                    if (errCode != ErrorCode.None)
                        return errCode;
                }
                return ErrorCode.None;
            }
            else
                return errCode;
        }

        public ErrorCode TryGet(string key, out byte[] value) // needs update to detect file records
        {
            if (LogWriter_ == null || MemStore_ == null)
            {
                value = [];
                return ErrorCode.UnInitializedInstance;
            }

            var errCode = MemStore_.TryGet(key, out value);

            if (errCode == ErrorCode.KeyNotFound)
            {
                for (int i = Frozen_.Count - 1; i >= 0; i--)
                {
                    KeyValueStore l = Frozen_[i];
                    errCode = l.TryGet(key, out value);
                    if (errCode == ErrorCode.None)
                        break;
                    if (errCode != ErrorCode.KeyNotFound)
                        return errCode;
                }
                foreach (var table in immutableSSTables)
                {
                    errCode = table.TryReadEntry(key, out value);
                    if (errCode == ErrorCode.KeyNotFound)
                        continue;

                    return errCode;
                }
            }

            if (errCode != ErrorCode.None)
                return errCode;

            return ErrorCode.None;
        }

        public ErrorCode Delete(string key)
        {
            if (LogWriter_ == null || MemStore_ == null)
                return ErrorCode.UnInitializedInstance;

            var errCode = WARecord.GetRecord(out WARecord record, WAOperation.DELETE, key, null);

            if (errCode == ErrorCode.None)
            {
                errCode = LogWriter_.Append(record);
                if (errCode != ErrorCode.None)
                    return errCode;
                errCode = MemStore_.Delete(key);
                if (errCode != ErrorCode.None)
                    return errCode;
                return ErrorCode.None;
            }
            else
                return errCode;
        }

        public ErrorCode ReplayRecords()
        {
            if (LogWriter_ == null || MemStore_ == null || LogReader_ == null)
                return ErrorCode.UnInitializedInstance;

            var errCode = LogReader_.ReadRecords(out var records);
            if (errCode != ErrorCode.None)
                return errCode;
            foreach (var record in records)
            {
                WAOperation op = record.Op;
                string key = record.KeyAsString;
                byte[] value = record.Value;

                errCode = op switch
                {
                    WAOperation.PUT => MemStore_.Put(key, value),
                    WAOperation.DELETE => MemStore_.Delete(key),
                    _ => ErrorCode.InvalidOperation,
                };

                if (errCode != ErrorCode.None)
                    return errCode;
            }
            return ErrorCode.None;
        }

        public ErrorCode SaveSnapshot(string path = @"./data/snapshot.dat")
        {
            if (Snapshot_ == null || MemStore_ == null || LogWriter_ == null)
                return ErrorCode.UnInitializedInstance;
            if (string.IsNullOrWhiteSpace(path))
                return ErrorCode.InvalidPath;
            var errCode = Snapshot_.SaveSnapshot(in MemStore_, path);
            if (errCode != ErrorCode.None)
                return errCode;
            errCode = LogWriter_.Truncate();
            if (errCode != ErrorCode.None)
                return errCode;
            return ErrorCode.None;
        }

        public ErrorCode LoadSnapshot(string path = @"./data/snapshot.dat")
        {
            if (Snapshot_ == null || MemStore_ == null)
                return ErrorCode.UnInitializedInstance;
            if (string.IsNullOrWhiteSpace(path))
                return ErrorCode.InvalidPath;
            var errCode = Snapshot_.LoadSnapshot(MemStore_, path);
            if (errCode != ErrorCode.None)
                return errCode;
            return ErrorCode.None;
        }

        const string SSTableBaseName = "SSTable";

        public ErrorCode FlushToSSTable()
        {
            if (MemStore_ == null || Path_ == null)
                return ErrorCode.UnInitializedInstance;

            ErrorCode errCode;

            var old = MemStore_;
            Frozen_.Add(old);
            MemStore_ = new();
            errCode = old.MakeImmutable();
            if (errCode != ErrorCode.None)
            {
                return errCode;
            }

            while (Frozen_.Count > 0)
            {
                old = Frozen_[0];
                errCode = old.GetReadOnly(out var keyValuePairs);
                if (errCode != ErrorCode.None)
                    return errCode;

                uint SerialNumber = Directory
                    .GetFiles(Path_, $"{SSTableBaseName}-*")
                    .Select(f =>
                        uint.TryParse(Path.GetFileName(f).Split('-')[^1], out uint n) ? n : 0
                    )
                    .DefaultIfEmpty(0u)
                    .Max();

                try
                {
                    var tmpPath = Path.Combine(Path_, $"{SSTableBaseName}-TMP");
                    var finalPath = Path.Combine(Path_, $"{SSTableBaseName}-{SerialNumber++:D5}");

                    errCode = SSTable.WriteTableToFile(
                        tmpPath,
                        keyValuePairs,
                        old.Count,
                        out ImmutableSSTable? ssTable
                    );
                    if (errCode != ErrorCode.None)
                        return errCode;

                    if (ssTable == null)
                        return ErrorCode.UnexpectedError;

                    MoveSSTable(tmpPath, finalPath, ref ssTable);

                    immutableSSTables.Add(ssTable);
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
