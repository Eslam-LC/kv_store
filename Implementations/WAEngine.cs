using kv_store.Enums;
using kv_store.Interfaces;

namespace kv_store.Implementations
{
    /*
    the Engine may write to wal and fail to write to memory store which is to be expected.
    also it will log records with invalid keys.
    */
    class WAEngine : IWriteAheadEngine
    {
        IWriteAheadLogger? fileLogger;
        IKeyValueStore? memStore;
        IWriteAheadReader? reader;
        ISnapshot? _snapshot;

        public Result Initialize(
            IWriteAheadLogger file_logger,
            IKeyValueStore memory_store,
            IWriteAheadReader log_reader,
            Snapshot snapshot
        )
        {
            if (file_logger == null || memory_store == null || log_reader == null)
                return new Result(ErrorCode.InvalidArguments);
            fileLogger = file_logger;
            memStore = memory_store;
            reader = log_reader;
            _snapshot = snapshot;
            return new Result(ErrorCode.None);
        }

        public Result Put(string key, byte[] value)
        {
            if (fileLogger == null || memStore == null)
                return new Result(ErrorCode.UnInitializedInstance);

            var errCode = WARecord.GetRecord(out WARecord record, WAOperation.PUT, key, value);

            if (errCode.Error == ErrorCode.None)
            {
                errCode = fileLogger.Append(record);
                if (errCode.Error != ErrorCode.None)
                    return errCode;
                errCode = memStore.Put(key, value);
                if (errCode.Error != ErrorCode.None)
                    return errCode;
                return new Result(ErrorCode.None);
            }
            else
                return errCode;
        }

        public Result TryGet(string key, out byte[] value) // needs update to detect file records
        {
            if (fileLogger == null || memStore == null)
            {
                value = [];
                return new Result(ErrorCode.UnInitializedInstance);
            }

            var errCode = memStore.TryGet(key, out value);
            if (errCode.Error != ErrorCode.None)
                return errCode;

            return new Result(ErrorCode.None);
        }

        public Result Delete(string key)
        {
            if (fileLogger == null || memStore == null)
                return new Result(ErrorCode.UnInitializedInstance);

            var errCode = WARecord.GetRecord(out WARecord record, WAOperation.DELETE, key, null);

            if (errCode.Error == ErrorCode.None)
            {
                errCode = fileLogger.Append(record);
                if (errCode.Error != ErrorCode.None)
                    return errCode;
                errCode = memStore.Delete(key);
                if (errCode.Error != ErrorCode.None)
                    return errCode;
                return new Result(ErrorCode.None);
            }
            else
                return errCode;
        }

        public Result ReplayRecords()
        {
            if (fileLogger == null || memStore == null || reader == null)
                return new Result(ErrorCode.UnInitializedInstance);

            var errCode = reader.ReadRecords(out var records);
            if (errCode.Error != ErrorCode.None)
                return errCode;
            foreach (var record in records)
            {
                errCode = WARecord.GetOpKeyValue(
                    record,
                    out WAOperation op,
                    out string key,
                    out byte[] value
                );
                if (errCode.Error != ErrorCode.None)
                    return errCode;

                switch (op)
                {
                    case WAOperation.PUT:
                        memStore.Put(key, value);
                        break;
                    case WAOperation.DELETE:
                        memStore.Delete(key);
                        break;
                    default:
                        return new Result(ErrorCode.InvalidOperation);
                }
            }
            return new Result(ErrorCode.None);
        }

        public Result SaveSnapshot(string path = @"./data/snapshot.dat")
        {
            if (_snapshot == null || memStore == null || fileLogger == null)
                return new Result(ErrorCode.UnInitializedInstance);
            if (string.IsNullOrWhiteSpace(path))
                return new Result(ErrorCode.InvalidPath);
            var errCode = _snapshot.SaveSnapshot(in memStore, path);
            if (errCode.Error != ErrorCode.None)
                return errCode;
            errCode = fileLogger.Truncate();
            if (errCode.Error != ErrorCode.None)
                return errCode;
            return new Result(ErrorCode.None);
        }

        public Result LoadSnapshot(string path = @"./data/snapshot.dat")
        {
            if (_snapshot == null || memStore == null)
                return new Result(ErrorCode.UnInitializedInstance);
            if (string.IsNullOrWhiteSpace(path))
                return new Result(ErrorCode.InvalidPath);
            var errCode = _snapshot.LoadSnapshot(memStore, path);
            if (errCode.Error != ErrorCode.None)
                return errCode;
            return new Result(ErrorCode.None);
        }
    }
}
