namespace kv_store.Enums
{
    public enum ErrorCode
    {
        None,
        ValueNotValid,
        KeyNotValid,
        EntryIsEmpty,
        InvalidPath,
        InvalidArguments,
        UnInitializedInstance,
        InvalidOperation,
        UnExpectedHashingFailure,
        CorruptedEntry,
        IOError,
        AccessDenied,
        UnexpectedError,
    }
}
