using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO; // Required for FileInfo
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupWarden.Models
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
        Warning             // The operation completed, but there are non-critical issues or warnings to review in the report (e.g., empty paths, inaccessible items that were skipped).
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

    public class PathIssue
    {
        public string PathSpec { get; } // The original path spec from AppConfig.Paths or the backup root
        public string? ExpandedPath { get; } // The expanded path, if available
        public PathIssueType IssueType { get; }
        public string Description { get; }

        public PathIssue(string pathSpec, string? expandedPath, PathIssueType issueType, string description)
        {
            PathSpec = pathSpec;
            ExpandedPath = expandedPath;
            IssueType = issueType;
            Description = description;
        }

        public override string ToString() => $"[{IssueType}] {(ExpandedPath ?? PathSpec)}: {Description}";
    }

    public enum FileDifferenceType
    {
        SourceOnly,                 // File exists in source, not in destination
        DestinationOnly,            // File exists in destination, not in source
        ContentMismatch,            // File exists in both, but size or timestamp differs
        OperationFailed             // An operation on this file failed (e.g. copy, delete)
    }

    public class FileDifference
    {
        public string RelativePath { get; }
        public FileDifferenceType DifferenceType { get; }
        public FileInfo? SourceFileInfo { get; } // Null if DestinationOnly
        public FileInfo? DestinationFileInfo { get; } // Null if SourceOnly
        public string Description { get; }

        public FileDifference(string relativePath, FileDifferenceType differenceType, string description, FileInfo? sourceFileInfo = null, FileInfo? destinationFileInfo = null)
        {
            RelativePath = relativePath;
            DifferenceType = differenceType;
            Description = description;
            SourceFileInfo = sourceFileInfo;
            DestinationFileInfo = destinationFileInfo;
        }
        public override string ToString() => $"[{DifferenceType}] {RelativePath}: {Description}";
    }

    public class AppSyncReport
    {
        public List<PathIssue> PathIssues { get; } = new List<PathIssue>();
        public List<FileDifference> FileDifferences { get; } = new List<FileDifference>();
        public SyncStatus OverallStatus { get; set; } = SyncStatus.Unknown;

        // Critical issues are those that likely prevent the core operation or indicate severe misconfiguration.
        public bool HasCriticalPathIssues => PathIssues.Any(pi =>
            pi.IssueType == PathIssueType.PathSpecNullOrEmpty ||
            pi.IssueType == PathIssueType.PathUnexpandable ||
            pi.IssueType == PathIssueType.PathInaccessible || // If a root path is inaccessible
            pi.IssueType == PathIssueType.OperationFailed);

        public bool HasFileOperationFailures => FileDifferences.Any(fd => fd.DifferenceType == FileDifferenceType.OperationFailed);

        public bool HasDifferencesExcludingFailures => FileDifferences.Any(fd => fd.DifferenceType != FileDifferenceType.OperationFailed);

        // Warnings are for issues that don't stop the process but should be noted,
        // or if an operation was deliberately prevented (like not clearing an empty source in SYNC).
        public bool HasWarningsOrPreventedOperations => PathIssues.Any(pi =>
            pi.IssueType == PathIssueType.PathIsEffectivelyEmpty ||
            pi.IssueType == PathIssueType.PathNotFound || // A missing path is a warning if the operation can still proceed with other paths
            pi.IssueType == PathIssueType.OperationPrevented);


        public void DetermineOverallStatus()
        {
            if (HasCriticalPathIssues || HasFileOperationFailures)
            {
                OverallStatus = SyncStatus.Failed; // If any critical issue or file operation failed
            }
            else if (HasDifferencesExcludingFailures)
            {
                OverallStatus = SyncStatus.OutOfSync; // If there are content differences
            }
            else if (HasWarningsOrPreventedOperations)
            {
                // If there are only warnings (like empty paths, or non-critical PathNotFound) or prevented ops, but no diffs or critical issues
                OverallStatus = SyncStatus.Warning;
            }
            else if (!PathIssues.Any() && !FileDifferences.Any()) // No issues of any kind, no differences
            {
                OverallStatus = SyncStatus.InSync;
            }
            else
            {
                // This case should ideally be covered by the above.
                // If there are path issues that are not critical and not warnings (e.g. an unhandled PathIssueType)
                // or some other combination.
                OverallStatus = SyncStatus.Unknown;
            }
        }
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Overall Status: {OverallStatus}");
            if (PathIssues.Any())
            {
                sb.AppendLine("Path Issues:");
                foreach (var issue in PathIssues) sb.AppendLine($"  - {issue}");
            }
            if (FileDifferences.Any())
            {
                sb.AppendLine("File Differences:");
                foreach (var diff in FileDifferences) sb.AppendLine($"  - {diff}");
            }
            if (!PathIssues.Any() && !FileDifferences.Any())
            {
                sb.AppendLine("No issues or differences found.");
            }
            return sb.ToString();
        }
    }

    public class BackupConfig
    {
        public List<AppConfig> Apps { get; set; } = [];
    }

    public partial class AppConfig : INotifyPropertyChanged
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<string> Paths { get; set; } = [];
        public string? Key { get; set; }
        public string? Account { get; set; }
        public string? Mods { get; set; }

        private SyncStatus _syncStatus = SyncStatus.Unknown;
        public SyncStatus SyncStatus
        {
            get => _syncStatus;
            set
            {
                if (_syncStatus != value)
                {
                    _syncStatus = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SyncStatus)));
                }
            }
        }

        private AppSyncReport? _lastSyncReport;
        public AppSyncReport? LastSyncReport // This could be used by UI to show details
        {
            get => _lastSyncReport;
            set
            {
                if (_lastSyncReport != value)
                {
                    _lastSyncReport = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastSyncReport)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
