using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Enums;

namespace kv_store.Implementations
{
    public class WAReader
    {
        public static ErrorCode ReadRecords(BinaryReader r, in KeyValueStore kvs)
        {
            if (r.BaseStream.Length == 0)
                return ErrorCode.FileIsEmpty;

            do
            {
                ErrorCode errorCode = WARecord.ReadFrame(r, out var op, out var key, out var value);
                if (errorCode != ErrorCode.None)
                    return errorCode; // Maybe Handle Returns like engine??

                if (op == null || key == null || (op == WAOperation.PUT && value == null))
                    return ErrorCode.UnexpectedError;

                errorCode = op switch
                {
                    WAOperation.PUT => kvs.Put(key, value!),
                    WAOperation.DELETE => kvs.Delete(key),
                    _ => ErrorCode.InvalidOperation,
                };

                if (errorCode == ErrorCode.KeyNotFound && op == WAOperation.DELETE)
                {
                    // if !FindKey return notFound.
                    // remmember to write this type of entry (delete found keys that are not in memory) in sstable
                    continue;
                }

                if (errorCode != ErrorCode.None)
                    return errorCode;
            } while (r.BaseStream.Position < r.BaseStream.Length);

            return ErrorCode.None;
        }
    }
}
