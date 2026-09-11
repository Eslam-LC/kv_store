using kv_store.EnumsAndConstants;
using static kv_store.EnumsAndConstants.ErrorCode;
using static kv_store.EnumsAndConstants.WAOperation;

namespace kv_store.Implementations
{
    public class WAReader
    {
        public static ErrorCode ReadRecords(BinaryReader r, in KeyValueStore kvs)
        {
            if (r.BaseStream.Length == 0)
                return FileIsEmpty;

            do
            {
                ErrorCode errorCode = WARecord.ReadFrame(r, out var op, out var key, out var value);
                if (errorCode != None)
                    return errorCode;

                // Read Frame guarntees non-null values if error code is none should start making documentation some day

                errorCode = op switch
                {
                    PUT => kvs.Put(key!, value!),
                    DELETE => kvs.Delete(key!),
                    _ => OperationIsInvalid,
                };

                if (errorCode != None)
                    return errorCode;
            } while (r.BaseStream.Position < r.BaseStream.Length);

            return None;
        }
    }
}
