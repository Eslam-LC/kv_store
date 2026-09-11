using kv_store.EnumsAndConstants;
using static kv_store.EnumsAndConstants.ErrorCode;
using static kv_store.EnumsAndConstants.WAOperation;

namespace kv_store.Implementations
{
    public class Snapshot
    {
        public static ErrorCode SaveSnapshot(BinaryWriter w, KeyValueStore store)
        {
            var errCode = store.GetImmutableKVList(out var ROKVL);
            if (errCode != None)
                return errCode;

            try
            {
                w.Write(store.Count); // deliberatly not using roDict.Count() to avoid unecessary O(n) traverse over the enumerable
                foreach (var (key, value) in ROKVL)
                {
                    errCode = WARecord.WriteFrame(w, value == null ? DELETE : PUT, key, value);
                    if (errCode != None)
                        return errCode;
                }
            }
            catch (IOException)
            {
                return InputOutputFailed;
            }
            catch (UnauthorizedAccessException)
            {
                return AccessDenied;
            }
            catch (Exception)
            {
                return UnexpectedFailure;
            }
            return None;
        }

        public static ErrorCode LoadSnapshot(BinaryReader r, KeyValueStore store)
        {
            if (r.BaseStream.Length == 0)
                return FileIsEmpty;

            try
            {
                Dictionary<string, byte[]?> tempDict = [];

                int count = r.ReadInt32();
                while (count-- > 0)
                {
                    var errCode = WARecord.ReadFrame(
                        r,
                        out WAOperation? op,
                        out string? key,
                        out byte[]? value
                    );
                    if (errCode != None)
                        return errCode;

                    if (key == null || op == null)
                        return UnexpectedFailure;

                    if (tempDict.ContainsKey(key))
                        return EntryIsCorrupted;

                    tempDict.Add(key, value);
                }

                store.BulkInitialize(tempDict);
            }
            catch (EndOfStreamException)
            {
                return EntryIsCorrupted;
            }
            catch (FileNotFoundException)
            {
                return PathIsInvalid;
            }
            catch (IOException)
            {
                return InputOutputFailed;
            }
            catch (UnauthorizedAccessException)
            {
                return AccessDenied;
            }
            catch (Exception)
            {
                return UnexpectedFailure;
            }
            return None;
        }
    }
}
