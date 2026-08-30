using kv_store.Enums;

namespace kv_store.Implementations
{
    /*
    the Engine may write to wal and fail to write to memory store which is to be expected.
    also it will log records with invalid keys.
    */
    class WAEngine
    {
        WAWriter? fileLogger;
        KeyValueStore? memStore;
        WAReader? reader;
        Snapshot? _snapshot;

        public ErrorCode Initialize(
            WAWriter file_logger,
            KeyValueStore memory_store,
            WAReader log_reader,
            Snapshot snapshot
        )
        {
            if (file_logger == null || memory_store == null || log_reader == null)
                return ErrorCode.InvalidArguments;
            fileLogger = file_logger;
            memStore = memory_store;
            reader = log_reader;
            _snapshot = snapshot;
            return ErrorCode.None;
        }

        public ErrorCode Put(string key, byte[] value)
        {
            if (fileLogger == null || memStore == null)
                return ErrorCode.UnInitializedInstance;

            var errCode = WARecord.GetRecord(out WARecord record, WAOperation.PUT, key, value);

            if (errCode == ErrorCode.None)
            {
                errCode = fileLogger.Append(record);
                if (errCode != ErrorCode.None)
                    return errCode;
                errCode = memStore.Put(key, value);
                if (errCode != ErrorCode.None)
                    return errCode;
                return ErrorCode.None;
            }
            else
                return errCode;
        }

        public ErrorCode TryGet(string key, out byte[] value) // needs update to detect file records
        {
            if (fileLogger == null || memStore == null)
            {
                value = [];
                return ErrorCode.UnInitializedInstance;
            }

            var errCode = memStore.TryGet(key, out value);
            if (errCode != ErrorCode.None)
                return errCode;

            return ErrorCode.None;
        }

        public ErrorCode Delete(string key)
        {
            if (fileLogger == null || memStore == null)
                return ErrorCode.UnInitializedInstance;

            var errCode = WARecord.GetRecord(out WARecord record, WAOperation.DELETE, key, null);

            if (errCode == ErrorCode.None)
            {
                errCode = fileLogger.Append(record);
                if (errCode != ErrorCode.None)
                    return errCode;
                errCode = memStore.Delete(key);
                if (errCode != ErrorCode.None)
                    return errCode;
                return ErrorCode.None;
            }
            else
                return errCode;
        }

        public ErrorCode ReplayRecords()
        {
            if (fileLogger == null || memStore == null || reader == null)
                return ErrorCode.UnInitializedInstance;

            var errCode = reader.ReadRecords(out var records);
            if (errCode != ErrorCode.None)
                return errCode;
            foreach (var record in records)
            {
                WAOperation op = record.Op;
                string key = record.KeyAsString;
                byte[] value = record.Value;

                errCode = op switch
                {
                    WAOperation.PUT => memStore.Put(key, value),
                    WAOperation.DELETE => memStore.Delete(key),
                    _ => ErrorCode.InvalidOperation,
                };

                if (errCode != ErrorCode.None)
                    return errCode;
            }
            return ErrorCode.None;
        }

        public ErrorCode SaveSnapshot(string path = @"./data/snapshot.dat")
        {
            if (_snapshot == null || memStore == null || fileLogger == null)
                return ErrorCode.UnInitializedInstance;
            if (string.IsNullOrWhiteSpace(path))
                return ErrorCode.InvalidPath;
            var errCode = _snapshot.SaveSnapshot(in memStore, path);
            if (errCode != ErrorCode.None)
                return errCode;
            errCode = fileLogger.Truncate();
            if (errCode != ErrorCode.None)
                return errCode;
            return ErrorCode.None;
        }

        public ErrorCode LoadSnapshot(string path = @"./data/snapshot.dat")
        {
            if (_snapshot == null || memStore == null)
                return ErrorCode.UnInitializedInstance;
            if (string.IsNullOrWhiteSpace(path))
                return ErrorCode.InvalidPath;
            var errCode = _snapshot.LoadSnapshot(memStore, path);
            if (errCode != ErrorCode.None)
                return errCode;
            return ErrorCode.None;
        }
    }
}
