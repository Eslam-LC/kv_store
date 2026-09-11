using static kv_store.Enums.ErrorCode;

namespace kv_store.Enums
{
    public enum ErrorCode
    {
        None,

        ValueIsInvalid,

        KeyIsInvalid,

        KeyWasNotFound,

        EntryIsEmpty,

        FileIsEmpty,

        PathIsInvalid,

        ArgumentsAreInvalid,

        InstanceIsNotInitialized,

        OperationIsInvalid,

        HashingFailedUnexpectedly,

        EntryIsCorrupted,

        InputOutputFailed,

        AccessDenied,

        UnexpectedFailure,

        CannotWriteToImmutableInstance,

        FileIsCorruptedOrVersionUnsupported,

        SstablesFailedToLoad,

        KeyWasDeleted,
    }

    static class MapExToEr
    {
        public static ErrorCode GetErrorCode(Exception ex) =>
            ex switch
            {
                null => UnexpectedFailure,
                PathTooLongException
                or NotSupportedException
                or DirectoryNotFoundException
                or FileNotFoundException => PathIsInvalid,
                UnauthorizedAccessException => AccessDenied,

                EndOfStreamException => EntryIsCorrupted,
                ObjectDisposedException => InstanceIsNotInitialized,
                IOException => InputOutputFailed,
                _ => UnexpectedFailure,
            };
    }
}
