using System.ComponentModel;

namespace kv_store.Enums
{
    public enum ErrorCode
    {
        [Description("No error occurred")]
        None,

        [Description("The provided value is not valid")]
        ValueNotValid,

        [Description("The provided key is not valid")]
        KeyNotValid,

        [Description("The specified key was not found")]
        KeyNotFound,

        [Description("Entry is empty")]
        EntryIsEmpty,

        [Description("The specified path is invalid")]
        InvalidPath,

        [Description("The arguments provided are invalid")]
        InvalidArguments,

        [Description("Instance has not been initialized")]
        UnInitializedInstance,

        [Description("The operation is invalid for the current state")]
        InvalidOperation,

        [Description("An unexpected hashing failure occurred")]
        UnExpectedHashingFailure,

        [Description("Entry is corrupted")]
        CorruptedEntry,

        [Description("An I/O error occurred")]
        IOError,

        [Description("Access to the resource was denied")]
        AccessDenied,

        [Description("An unexpected error occurred")]
        UnexpectedError,
    }
}
