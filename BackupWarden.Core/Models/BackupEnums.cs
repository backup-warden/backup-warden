namespace BackupWarden.Core.Models
{
    public enum SyncMode
    {
        Copy, // Files from the source are copied to the destination. If files with the same names are present on the destination, they are overwritten.
        Sync  // Files on the destination are changed to match those on thesource. If a file does not exist on the source, it is also deleted from the destination.
    }

    public enum SyncStatus
    {
        Unknown,            // Initial state or error determining status
        InSync,             // Backup accurately reflects the source application paths.
        OutOfSync,          // Backup does not accurately reflect the source application paths (e.g., source has newer/different files, or backup has files not in source).
        Syncing,            // A backup or restore operation is in progress.
        Failed,             // The last operation failed.
        Warning,            // The operation completed, but there are non-critical issues or warnings to review in the report (e.g., empty paths, inaccessible items that were skipped).
        NotYetBackedUp      // The application has configured paths, but no backup has been performed yet.
    }

    public enum PathIssueType
    {
        PathSpecNullOrEmpty,    // The original path string in AppConfig.Paths was null or empty
        PathUnexpandable,       // A special folder path (e.g., %AppData%) could not be expanded
        PathNotFound,           // The expanded path (file or directory) does not exist
        PathInaccessible,       // The path exists but cannot be accessed (e.g., permissions)
        PathIsEffectivelyEmpty, // A configured directory path exists but contains no files (can be a warning or info)
        OperationPrevented,     // An issue prevented an operation (e.g. source empty in SYNC mode, preventing destination clear)
        OperationFailed         // A specific file/directory operation failed (e.g. copy, delete)
    }

    public enum FileDifferenceType
    {
        OnlyInApplication,      // File exists only in the application's paths, not in the backup.
        OnlyInBackup,           // File exists only in the backup, not in the application's paths.
        ContentMismatch,        // File exists in both, but size or timestamp differs.
        OperationFailed         // An operation on this file failed (e.g. copy, delete).
    }

    public enum PathIssueSource
    {
        Application,        // Issue relates to the application's source paths
        BackupLocation,     // Issue relates to the backup destination paths
        Operation           // Issue relates to the overall operation rather than a specific path set
    }
}