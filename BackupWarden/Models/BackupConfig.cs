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

    public class PathIssue
    {
        public string PathSpec { get; }
        public string? ExpandedPath { get; }
        public PathIssueType IssueType { get; }
        public PathIssueSource Source { get; }
        public string Description { get; }

        public PathIssue(string pathSpec, string? expandedPath, PathIssueType issueType, PathIssueSource source, string description)
        {
            PathSpec = pathSpec;
            ExpandedPath = expandedPath;
            IssueType = issueType;
            Source = source;
            Description = description;
        }

        public override string ToString()
        {
            string mainPathToDisplay = ExpandedPath ?? PathSpec;
            string displayDescription = Description;

            if (!string.IsNullOrEmpty(mainPathToDisplay))
            {
                string quotedMainPath = $"'{mainPathToDisplay}'";
                if (displayDescription.Contains(quotedMainPath))
                {
                    displayDescription = displayDescription.Replace(quotedMainPath, "(this path)");
                }
            }
            return $"[{Source.ToDisplayString()} - {IssueType.ToDisplayString()}] {mainPathToDisplay}: {displayDescription}";
        }
    }

    public class FileDifference
    {
        public string RelativePath { get; }
        public FileDifferenceType DifferenceType { get; }
        public FileInfo? ApplicationFileInfo { get; }
        public FileInfo? BackupFileInfo { get; }
        public string Description { get; }

        public FileDifference(string relativePath, FileDifferenceType differenceType, string description, FileInfo? applicationFileInfo = null, FileInfo? backupFileInfo = null)
        {
            RelativePath = relativePath;
            DifferenceType = differenceType;
            Description = description;
            ApplicationFileInfo = applicationFileInfo;
            BackupFileInfo = backupFileInfo;
        }

        public override string ToString() => $"[{DifferenceType.ToDisplayString()}] {RelativePath}: {Description}";
    }

    public class AppSyncReport
    {
        public List<PathIssue> PathIssues { get; } = [];
        public List<FileDifference> FileDifferences { get; } = [];
        public SyncStatus OverallStatus { get; set; } = SyncStatus.Unknown;
        public string AppBackupRootPath { get; internal set; } = string.Empty;

        public bool HasCriticalPathIssues => PathIssues.Any(pi =>
            (pi.Source == PathIssueSource.Application && (
                pi.IssueType == PathIssueType.PathSpecNullOrEmpty ||
                pi.IssueType == PathIssueType.PathUnexpandable ||
                pi.IssueType == PathIssueType.PathInaccessible
            )) ||
            (pi.IssueType == PathIssueType.PathNotFound && pi.Source == PathIssueSource.BackupLocation && !string.IsNullOrEmpty(AppBackupRootPath) && pi.PathSpec == AppBackupRootPath) || // Main backup dir missing is critical unless it's a NotYetBackedUp scenario
            pi.IssueType == PathIssueType.OperationFailed); // General operation failures

        public bool HasFileOperationFailures => FileDifferences.Any(fd => fd.DifferenceType == FileDifferenceType.OperationFailed);

        public bool HasDifferencesExcludingFailures => FileDifferences.Any(fd => fd.DifferenceType != FileDifferenceType.OperationFailed);

        public bool HasWarningsOrPreventedOperations => PathIssues.Any(pi =>
            pi.IssueType == PathIssueType.PathIsEffectivelyEmpty ||
            // PathNotFound is a warning if it's not the main AppBackupRootPath (which would be critical or NotYetBackedUp)
            (pi.IssueType == PathIssueType.PathNotFound && !(pi.Source == PathIssueSource.BackupLocation && !string.IsNullOrEmpty(AppBackupRootPath) && pi.PathSpec == AppBackupRootPath)) ||
            pi.IssueType == PathIssueType.OperationPrevented ||
            (pi.IssueType == PathIssueType.PathInaccessible && pi.Source == PathIssueSource.BackupLocation)); // Inaccessible backup sub-paths are warnings


        public void DetermineOverallStatus()
        {
            // Condition 1 for NotYetBackedUp: The main backup root for the app is missing.
            bool appSpecificBackupRootMissing = PathIssues.Any(pi =>
                pi.Source == PathIssueSource.BackupLocation &&
                pi.IssueType == PathIssueType.PathNotFound &&
                !string.IsNullOrEmpty(AppBackupRootPath) &&
                pi.PathSpec == AppBackupRootPath);

            // Condition 2 for NotYetBackedUp: The app has configured paths (not an empty config).
            // This checks if the initial PathIssue for "No path specifications provided" with PathSpec "N/A" for Application source exists.
            bool appHasConfiguredPaths = !PathIssues.Any(pi =>
                pi.Source == PathIssueSource.Application &&
                pi.IssueType == PathIssueType.PathSpecNullOrEmpty &&
                pi.PathSpec == "N/A");

            // Condition 3 for NotYetBackedUp: No critical issues with the application's own source paths.
            bool criticalAppSourcePathIssues = PathIssues.Any(pi =>
                pi.Source == PathIssueSource.Application &&
                (pi.IssueType == PathIssueType.PathSpecNullOrEmpty || // Check for individual null/empty paths
                 pi.IssueType == PathIssueType.PathUnexpandable ||
                 pi.IssueType == PathIssueType.PathInaccessible));

            // Condition 4 for NotYetBackedUp: No general operation failures or file operation failures.
            bool generalOrFileOpFailures = PathIssues.Any(pi => pi.IssueType == PathIssueType.OperationFailed) || HasFileOperationFailures;

            if (appSpecificBackupRootMissing && appHasConfiguredPaths && !criticalAppSourcePathIssues && !generalOrFileOpFailures)
            {
                OverallStatus = SyncStatus.NotYetBackedUp;
            }
            // If not "NotYetBackedUp", then evaluate other statuses.
            // HasCriticalPathIssues will correctly consider a missing backup root as critical if this isn't a "NotYetBackedUp" scenario.
            else if (HasCriticalPathIssues || HasFileOperationFailures)
            {
                OverallStatus = SyncStatus.Failed;
            }
            else if (HasDifferencesExcludingFailures)
            {
                OverallStatus = SyncStatus.OutOfSync;
            }
            else if (HasWarningsOrPreventedOperations)
            {
                OverallStatus = SyncStatus.Warning;
            }
            else if (PathIssues.Count == 0 && FileDifferences.Count == 0)
            {
                OverallStatus = SyncStatus.InSync;
            }
            else
            {
                // If it falls through, and NotYetBackedUp was set, it means there were other non-critical/non-warning issues.
                // If NotYetBackedUp was set and it was the *only* issue (e.g. appSpecificBackupRootMissing was the only PathIssue),
                // it would have been caught by the first 'if'.
                // This 'else' is a fallback.
                OverallStatus = SyncStatus.Unknown;
            }
        }

        public string ToSummaryString(int maxDistinctTypesToShow = 2)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Status: {OverallStatus.ToDisplayString()}");

            if (PathIssues.Any())
            {
                var issuesBySource = PathIssues.GroupBy(i => i.Source);
                foreach (var group in issuesBySource.OrderBy(g => g.Key))
                {
                    sb.Append($"• {group.Key.ToDisplayString()} Path Issues: {group.Count()}");
                    var topPathIssues = group
                        .GroupBy(i => i.IssueType)
                        .Select(g => new { Type = g.Key, Count = g.Count() })
                        .OrderByDescending(x => x.Count)
                        .Take(maxDistinctTypesToShow)
                        .ToList();
                    if (topPathIssues.Any())
                    {
                        sb.Append(" (");
                        sb.Append(string.Join(", ", topPathIssues.Select(tpi => $"{tpi.Count} {tpi.Type.ToDisplayString()}")));
                        if (group.GroupBy(i => i.IssueType).Count() > maxDistinctTypesToShow)
                        {
                            sb.Append(", ...");
                        }
                        sb.Append(")");
                    }
                    sb.AppendLine();
                }
            }

            if (FileDifferences.Any())
            {
                sb.Append($"• File Differences: {FileDifferences.Count}");
                var topFileDiffs = FileDifferences
                    .GroupBy(d => d.DifferenceType)
                    .Select(g => new { Type = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(maxDistinctTypesToShow)
                    .ToList();
                if (topFileDiffs.Any())
                {
                    sb.Append(" (");
                    sb.Append(string.Join(", ", topFileDiffs.Select(tfd => $"{tfd.Count} {tfd.Type.ToDisplayString()}")));
                    if (FileDifferences.GroupBy(d => d.DifferenceType).Count() > maxDistinctTypesToShow)
                    {
                        sb.Append(", ...");
                    }
                    sb.Append(")");
                }
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Overall Status: {OverallStatus.ToDisplayString()}");
            if (PathIssues.Any())
            {
                sb.AppendLine("Path Issues:");
                foreach (var issue in PathIssues.OrderBy(i => i.Source).ThenBy(i => i.IssueType))
                {
                    sb.AppendLine($"  - {issue}");
                }
            }
            if (FileDifferences.Any())
            {
                sb.AppendLine("File Differences:");
                foreach (var diff in FileDifferences.OrderBy(d => d.DifferenceType).ThenBy(d => d.RelativePath))
                {
                    sb.AppendLine($"  - {diff}");
                }
            }

            // Special message for NotYetBackedUp if it's the primary state and no other significant issues.
            if (OverallStatus == SyncStatus.NotYetBackedUp)
            {
                bool onlyAppRootMissingIssue = PathIssues.Count() == 1 && PathIssues.First().IssueType == PathIssueType.PathNotFound && PathIssues.First().Source == PathIssueSource.BackupLocation && PathIssues.First().PathSpec == AppBackupRootPath;
                if (onlyAppRootMissingIssue && !FileDifferences.Any())
                {
                    sb.AppendLine("No backup performed for this application yet.");
                }
                // If there are other issues/differences, they will be listed above.
            }
            else if (PathIssues.Count == 0 && FileDifferences.Count == 0) // For other statuses like InSync
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
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SyncStatusDisplay)));
                }
            }
        }
        public string SyncStatusDisplay => SyncStatus.ToDisplayString();

        private AppSyncReport? _lastSyncReport;
        public AppSyncReport? LastSyncReport
        {
            get => _lastSyncReport;
            set
            {
                if (_lastSyncReport != value)
                {
                    _lastSyncReport = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastSyncReport)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastSyncReportSummary)));
                }
            }
        }

        public string? LastSyncReportSummary => LastSyncReport?.ToSummaryString();

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}