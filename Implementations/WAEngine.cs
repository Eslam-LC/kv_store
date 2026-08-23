using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Interfaces;

namespace kv_store.Implementations
{
    /*
    the Engine may write to wal and fail to write to memory store which is to be expected.
    also it will log records with invalid keys.
    */
    public class WAEngine : IWriteAheadEngine
    {
        IWriteAheadLogger? fileLogger;
        IKeyValueStore? memStore;

        public Result Initialize(IWriteAheadLogger file_logger, IKeyValueStore memory_store)
        {
            if (file_logger == null || memory_store == null)
                return new Result(Enums.ErrorCode.InvalidArguments);
            fileLogger = file_logger;
            memStore = memory_store;
            return new Result(Enums.ErrorCode.None);
        }

        public Result Put(string key, byte[] value)
        {
            if (fileLogger == null || memStore == null)
                return new Result(Enums.ErrorCode.UnInitializedInstance);

            var errCode = WARecord.GetRecord(
                out WARecord record,
                Enums.WAOperation.PUT,
                key,
                value
            );

            if (errCode.Error == Enums.ErrorCode.None)
            {
                errCode = fileLogger.Append(record);
                if (errCode.Error != Enums.ErrorCode.None)
                    return errCode;
                errCode = memStore.Put(key, value);
                if (errCode.Error != Enums.ErrorCode.None)
                    return errCode;
                return new Result(Enums.ErrorCode.None);
            }
            else
                return errCode;
        }

        public Result TryGet(string key, out byte[] value)
        {
            if (fileLogger == null || memStore == null)
            {
                value = [];
                return new Result(Enums.ErrorCode.UnInitializedInstance);
            }

            var errCode = memStore.TryGet(key, out value);
            if (errCode.Error != Enums.ErrorCode.None)
                return errCode;

            return new Result(Enums.ErrorCode.None);
        }

        public Result Delete(string key)
        {
            if (fileLogger == null || memStore == null)
                return new Result(Enums.ErrorCode.UnInitializedInstance);

            var errCode = WARecord.GetRecord(
                out WARecord record,
                Enums.WAOperation.DELETE,
                key,
                null
            );

            if (errCode.Error == Enums.ErrorCode.None)
            {
                errCode = fileLogger.Append(record);
                if (errCode.Error != Enums.ErrorCode.None)
                    return errCode;
                errCode = memStore.Delete(key);
                if (errCode.Error != Enums.ErrorCode.None)
                    return errCode;
                return new Result(Enums.ErrorCode.None);
            }
            else
                return errCode;
        }
    }
}
