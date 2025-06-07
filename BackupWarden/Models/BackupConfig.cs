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

        private bool AppSourceIsFatallyFlawed => PathIssues.Any(pi =>
                    pi.Source == PathIssueSource.Application &&
                    ((pi.IssueType == PathIssueType.PathSpecNullOrEmpty && pi.PathSpec == "N/A") || // AppConfig.Paths was empty
                     pi.IssueType == PathIssueType.PathUnexpandable ||
                     pi.IssueType == PathIssueType.PathInaccessible));

        private bool HasOperationalFailures =>
            PathIssues.Any(pi => pi.IssueType == PathIssueType.OperationFailed) ||
            FileDifferences.Any(fd => fd.DifferenceType == FileDifferenceType.OperationFailed);

        private bool IsMissingBackupRoot => PathIssues.Any(pi =>
                    pi.Source == PathIssueSource.BackupLocation &&
                    pi.IssueType == PathIssueType.PathNotFound &&
            !string.IsNullOrEmpty(AppBackupRootPath) &&
            pi.PathSpec == AppBackupRootPath);

        private bool HasDataDifferencesExcludingOnlyInApp => FileDifferences.Any(fd =>
                    fd.DifferenceType == FileDifferenceType.OnlyInBackup || // OnlyInBackup is a data difference
                    fd.DifferenceType == FileDifferenceType.ContentMismatch); // ContentMismatch is a data difference
                                                                      // OnlyInApplication is handled separately for NotYetBackedUp vs OutOfSync

        public void DetermineOverallStatus()
        {
            // Priority 1: Hard Failures (Fatal app config issues or operational/file failures)
            if (AppSourceIsFatallyFlawed || HasOperationalFailures)
            {
                OverallStatus = SyncStatus.Failed;
                return;
            }

            // Priority 2: Not Yet Backed Up
            bool appHasAnyConfiguredPaths = !PathIssues.Any(pi =>
                pi.Source == PathIssueSource.Application &&
                pi.IssueType == PathIssueType.PathSpecNullOrEmpty &&
                pi.PathSpec == "N/A"); // True if app.Paths was not empty

            if (IsMissingBackupRoot)
            {
                if (appHasAnyConfiguredPaths)
                {
                    // For "NotYetBackedUp", all file differences must be "OnlyInApplication".
                    bool onlyAppSourceFileDifferences = FileDifferences.All(fd => fd.DifferenceType == FileDifferenceType.OnlyInApplication);

                    // All path issues must be either the missing backup root itself, or non-fatal application issues.
                    // Fatal app issues and operational failures are already caught.
                    bool pathIssuesAreConsistent = PathIssues.All(pi =>
                        (pi.Source == PathIssueSource.BackupLocation && pi.IssueType == PathIssueType.PathNotFound && pi.PathSpec == AppBackupRootPath) || // The expected missing backup root
                        (pi.Source == PathIssueSource.Application) // Any remaining app-side issues (non-fatal as per prior checks)
                    );
                    // We must actually have the "missing backup root" issue.
                    bool hasTheMissingBackupRootIssue = PathIssues.Any(pi => pi.Source == PathIssueSource.BackupLocation && pi.IssueType == PathIssueType.PathNotFound && pi.PathSpec == AppBackupRootPath);


                    if (hasTheMissingBackupRootIssue && pathIssuesAreConsistent && onlyAppSourceFileDifferences)
                    {
                        OverallStatus = SyncStatus.NotYetBackedUp;
                        return;
                    }
                }
                // If backup root is missing but conditions for NotYetBackedUp are not cleanly met, it's a Failed state.
                OverallStatus = SyncStatus.Failed;
                return;
            }

            // At this point: App source is not fatally flawed, no operational failures, and backup root exists.

            // Priority 3: OutOfSync
            // If there are any data differences (OnlyInBackup, ContentMismatch) OR if there are OnlyInApplication files (since backup exists)
            if (HasDataDifferencesExcludingOnlyInApp || FileDifferences.Any(fd => fd.DifferenceType == FileDifferenceType.OnlyInApplication))
            {
                OverallStatus = SyncStatus.OutOfSync;
                return;
            }

            // Priority 4: Warnings
            // No data differences at this point. Check for warning-level path issues.
            bool hasWarningPathIssues = PathIssues.Any(pi =>
                pi.IssueType == PathIssueType.PathIsEffectivelyEmpty ||
                (pi.IssueType == PathIssueType.PathNotFound && pi.Source == PathIssueSource.Application) || // App sub-path not found
                (pi.IssueType == PathIssueType.PathNotFound && pi.Source == PathIssueSource.BackupLocation) || // Backup sub-path not found (root is present)
                pi.IssueType == PathIssueType.OperationPrevented ||
                (pi.IssueType == PathIssueType.PathInaccessible && pi.Source == PathIssueSource.BackupLocation) // Backup sub-path inaccessible
            );

            if (hasWarningPathIssues)
            {
                OverallStatus = SyncStatus.Warning;
                return;
            }

            // Priority 5: InSync
            // If we reach here: no failures, not NotYetBackedUp, backup root exists,
            // no data differences, and no warning-level path issues.
            // This implies PathIssues and FileDifferences are both effectively empty of problems.
            if (!PathIssues.Any() && !FileDifferences.Any())
            {
                OverallStatus = SyncStatus.InSync;
                return;
            }

            // Fallback
            OverallStatus = SyncStatus.Unknown;
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

            if (OverallStatus == SyncStatus.NotYetBackedUp)
            {
                // For NotYetBackedUp, we expect the missing backup root issue and potentially "OnlyInApplication" file differences.
                // If these are the *only* things, the specific message is good. Otherwise, the listed issues/diffs provide detail.
                bool onlyExpectedElementsForNotYetBackedUp =
                    PathIssues.All(pi => (pi.Source == PathIssueSource.BackupLocation && pi.IssueType == PathIssueType.PathNotFound && pi.PathSpec == AppBackupRootPath) || pi.Source == PathIssueSource.Application) &&
                    PathIssues.Any(pi => pi.Source == PathIssueSource.BackupLocation && pi.IssueType == PathIssueType.PathNotFound && pi.PathSpec == AppBackupRootPath) &&
                    FileDifferences.All(fd => fd.DifferenceType == FileDifferenceType.OnlyInApplication);

                if (onlyExpectedElementsForNotYetBackedUp &&
                    // And if the only path issues are the missing root and/or non-critical app issues
                    PathIssues.Count(pi => !(pi.Source == PathIssueSource.BackupLocation && pi.IssueType == PathIssueType.PathNotFound && pi.PathSpec == AppBackupRootPath)) == PathIssues.Count(pi => pi.Source == PathIssueSource.Application))
                {
                    // If only the "NotYetBackedUp" condition is met (main backup folder missing)
                    // and any file differences are "OnlyInApplication", and any other path issues are non-critical app issues.
                    bool onlyMissingRootAndAppIssues = PathIssues.All(pi =>
                        (pi.Source == PathIssueSource.BackupLocation && pi.IssueType == PathIssueType.PathNotFound && pi.PathSpec == AppBackupRootPath) ||
                        (pi.Source == PathIssueSource.Application && (pi.IssueType == PathIssueType.PathIsEffectivelyEmpty || pi.IssueType == PathIssueType.PathNotFound))
                    );
                    if (onlyMissingRootAndAppIssues && FileDifferences.All(fd => fd.DifferenceType == FileDifferenceType.OnlyInApplication))
                    {
                        sb.AppendLine("Application data is present; no backup performed yet.");
                    }
                    // Otherwise, the listed issues/differences will provide the necessary detail.
                }
            }
            else if (PathIssues.Count == 0 && FileDifferences.Count == 0)
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

        public AppSyncReport? LastSyncReport { get; set; }

        private string? _lastSyncReportSummary;
        public string? LastSyncReportSummary
        {
            get => _lastSyncReportSummary;
            set
            {
                if (_lastSyncReportSummary != value)
                {
                    _lastSyncReportSummary = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastSyncReportSummary)));
                }
            }
        }

        private string? _lastSyncReportDetail;
        public string? LastSyncReportDetail
        {
            get => _lastSyncReportDetail;
            set
            {
                if (_lastSyncReportDetail != value)
                {
                    _lastSyncReportDetail = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastSyncReportDetail)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}