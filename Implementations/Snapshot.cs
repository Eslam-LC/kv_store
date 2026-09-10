using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using kv_store.Enums;

namespace kv_store.Implementations
{
    public class Snapshot
    {
        public static ErrorCode SaveSnapshot(BinaryWriter w, in KeyValueStore store)
        {
            var errCode = store.GetImmutableKVList(out var ROKVL);
            if (errCode != ErrorCode.None)
                return errCode;

            try
            {
                w.Write(store.Count); // deliberatly not using roDict.Count() to avoid unecessary O(n) traverse over the enumerable
                foreach (var (key, value) in ROKVL)
                {
                    errCode = WARecord.WriteFrame(
                        w,
                        value == null ? WAOperation.DELETE : WAOperation.PUT,
                        key,
                        value
                    );
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

        public static ErrorCode LoadSnapshot(BinaryReader r, KeyValueStore store)
        {
            if (r.BaseStream.Length == 0)
                return ErrorCode.FileIsEmpty;

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
                    if (errCode != ErrorCode.None)
                        return errCode;

                    if (key == null || op == null)
                        return ErrorCode.UnexpectedError;

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
