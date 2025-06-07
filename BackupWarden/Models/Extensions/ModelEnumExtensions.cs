using BackupWarden.Models;

namespace BackupWarden.Models.Extensions
{
    public static class ModelEnumExtensions
    {
        public static string ToDisplayString(this SyncMode mode) => mode switch
        {
            SyncMode.Copy => "Copy Mode",
            SyncMode.Sync => "Sync Mode",
            _ => mode.ToString()
        };

        public static string ToDisplayString(this SyncStatus status) => status switch
        {
            SyncStatus.Unknown => "Unknown",
            SyncStatus.InSync => "In Sync",
            SyncStatus.OutOfSync => "Out of Sync",
            SyncStatus.Syncing => "Syncing in Progress",
            SyncStatus.Failed => "Operation Failed",
            SyncStatus.Warning => "Completed with Warnings",
            SyncStatus.NotYetBackedUp => "Not Yet Backed Up",
            _ => status.ToString()
        };

        public static string ToDisplayString(this PathIssueType issueType) => issueType switch
        {
            PathIssueType.PathSpecNullOrEmpty => "Path Not Specified",
            PathIssueType.PathUnexpandable => "Path Unexpandable",
            PathIssueType.PathNotFound => "Path Not Found",
            PathIssueType.PathInaccessible => "Path Inaccessible",
            PathIssueType.PathIsEffectivelyEmpty => "Directory Empty",
            PathIssueType.OperationPrevented => "Operation Prevented",
            PathIssueType.OperationFailed => "Operation Failed on Path",
            _ => issueType.ToString()
        };

        public static string ToDisplayString(this FileDifferenceType diffType) => diffType switch
        {
            FileDifferenceType.OnlyInApplication => "Only in App",
            FileDifferenceType.OnlyInBackup => "Only in Backup",
            FileDifferenceType.ContentMismatch => "Content Mismatch",
            FileDifferenceType.OperationFailed => "Operation Failed on File",
            _ => diffType.ToString()
        };

        public static string ToDisplayString(this PathIssueSource source) => source switch
        {
            PathIssueSource.Application => "App",
            PathIssueSource.BackupLocation => "Backup",
            PathIssueSource.Operation => "Operation",
            _ => source.ToString()
        };
    }
}