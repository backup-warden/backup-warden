using BackupWarden.Models;
using BackupWarden.Utils;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text; // Required for StringBuilder in AppSyncReport
using System.Threading.Tasks;

namespace BackupWarden.Services.Business
{
    public interface IBackupSyncService
    {
        Task UpdateSyncStatusAsync(IEnumerable<AppConfig> apps, string destinationRoot, Action<AppConfig, AppSyncReport>? perAppStatusCallback = null);
        Task RestoreAsync(IEnumerable<AppConfig> configs, string destinationRoot, SyncMode mode, IProgress<int>? progress = null, Action<AppConfig, AppSyncReport>? perAppStatusCallback = null);
        Task BackupAsync(IEnumerable<AppConfig> configs, string destinationRoot, SyncMode mode, IProgress<int>? progress = null, Action<AppConfig, AppSyncReport>? perAppStatusCallback = null);
    }

    public class BackupSyncService : IBackupSyncService
    {
        private static readonly AsyncRetryPolicy _retryPolicy = Policy
            .Handle<IOException>()
            .WaitAndRetryAsync(5, retryAttempt => TimeSpan.FromMilliseconds(500));

        private readonly ILogger<BackupSyncService> _logger;

        public BackupSyncService(ILogger<BackupSyncService> logger)
        {
            _logger = logger;
        }

        private static bool AreFileTimesClose(DateTime t1, DateTime t2, TimeSpan? tolerance = null)
        {
            tolerance ??= TimeSpan.FromSeconds(2);
            return (t1 > t2) ? (t1 - t2 <= tolerance) : (t2 - t1 <= tolerance);
        }

        private (Dictionary<string, FileInfo> FilesByRelativePath, List<PathIssue> Issues, bool IsEffectivelyEmptyOverall) GetPathContents(
            IEnumerable<string> pathSpecs,
            Func<string, string> getRelativePathFunc,
            string? baseDirectoryForRelativePath = null)
        {
            var fileDetails = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            var issues = new List<PathIssue>();
            bool isEffectivelyEmptyOverall = true;
            bool anyValidPathSpecEncountered = false; // Tracks if we found at least one non-null/empty/unexpandable pathSpec

            if (pathSpecs == null || !pathSpecs.Any())
            {
                issues.Add(new PathIssue("N/A", null, PathIssueType.PathSpecNullOrEmpty, "No path specifications provided."));
                return (fileDetails, issues, true);
            }

            foreach (var pathSpec in pathSpecs)
            {
                if (string.IsNullOrWhiteSpace(pathSpec))
                {
                    issues.Add(new PathIssue(pathSpec ?? "NULL", null, PathIssueType.PathSpecNullOrEmpty, "Path specification is null or whitespace."));
                    continue;
                }

                var expandedPath = SpecialFolderUtil.ExpandSpecialFolders(pathSpec);
                if (string.IsNullOrWhiteSpace(expandedPath))
                {
                    issues.Add(new PathIssue(pathSpec, null, PathIssueType.PathUnexpandable, $"Path specification '{pathSpec}' could not be expanded."));
                    continue;
                }

                anyValidPathSpecEncountered = true;
                bool isPathDirectorySpec = expandedPath.EndsWith(Path.DirectorySeparatorChar.ToString()) ||
                                           expandedPath.EndsWith(Path.AltDirectorySeparatorChar.ToString());
                var actualPath = isPathDirectorySpec ? expandedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : expandedPath;

                if (isPathDirectorySpec)
                {
                    if (!Directory.Exists(actualPath))
                    {
                        issues.Add(new PathIssue(pathSpec, actualPath, PathIssueType.PathNotFound, $"Directory '{actualPath}' (from '{pathSpec}') not found."));
                        continue;
                    }
                    try
                    {
                        var filesInDir = Directory.EnumerateFiles(actualPath, "*", SearchOption.AllDirectories).ToList();
                        if (filesInDir.Any())
                        {
                            // isEffectivelyEmptyOverall will be set to false later if any fileDetails are added
                        }
                        else
                        {
                            issues.Add(new PathIssue(pathSpec, actualPath, PathIssueType.PathIsEffectivelyEmpty, $"Directory '{actualPath}' (from '{pathSpec}') is empty."));
                        }

                        foreach (var file in filesInDir)
                        {
                            var fi = new FileInfo(file);
                            var relPath = baseDirectoryForRelativePath != null ? Path.GetRelativePath(baseDirectoryForRelativePath, file) : getRelativePathFunc(file);
                            if (!fileDetails.ContainsKey(relPath))
                            {
                                fileDetails[relPath] = fi;
                            }
                            else
                            {
                                _logger.LogDebug("Duplicate relative path '{RelativePath}' from spec '{PathSpec}' (expanded: {ExpandedPath}). Keeping first entry.", relPath, pathSpec, file);
                            }
                        }
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        issues.Add(new PathIssue(pathSpec, actualPath, PathIssueType.PathInaccessible, $"Access denied to directory '{actualPath}' (from '{pathSpec}'). Error: {ex.Message}"));
                    }
                    catch (DirectoryNotFoundException ex)
                    {
                        issues.Add(new PathIssue(pathSpec, actualPath, PathIssueType.PathNotFound, $"Directory '{actualPath}' (from '{pathSpec}') not found during enumeration. Error: {ex.Message}"));
                    }
                    catch (Exception ex)
                    {
                        issues.Add(new PathIssue(pathSpec, actualPath, PathIssueType.PathInaccessible, $"Error enumerating directory '{actualPath}' (from '{pathSpec}'). Error: {ex.Message}"));
                    }
                }
                else // File
                {
                    if (!File.Exists(actualPath))
                    {
                        issues.Add(new PathIssue(pathSpec, actualPath, PathIssueType.PathNotFound, $"File '{actualPath}' (from '{pathSpec}') not found."));
                        continue;
                    }
                    try
                    {
                        var fi = new FileInfo(actualPath);
                        var relPath = baseDirectoryForRelativePath != null ? Path.GetRelativePath(baseDirectoryForRelativePath, actualPath) : getRelativePathFunc(actualPath);
                        if (!fileDetails.ContainsKey(relPath))
                        {
                            fileDetails[relPath] = fi;
                        }
                        else
                        {
                            _logger.LogDebug("Duplicate relative path '{RelativePath}' for file spec '{PathSpec}' (expanded: {ExpandedPath}). Keeping first entry.", relPath, pathSpec, actualPath);
                        }
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        issues.Add(new PathIssue(pathSpec, actualPath, PathIssueType.PathInaccessible, $"Access denied to file '{actualPath}' (from '{pathSpec}'). Error: {ex.Message}"));
                    }
                    catch (FileNotFoundException ex)
                    {
                        issues.Add(new PathIssue(pathSpec, actualPath, PathIssueType.PathNotFound, $"File '{actualPath}' (from '{pathSpec}') not found during info retrieval. Error: {ex.Message}"));
                    }
                    catch (Exception ex)
                    {
                        issues.Add(new PathIssue(pathSpec, actualPath, PathIssueType.PathInaccessible, $"Error accessing file '{actualPath}' (from '{pathSpec}'). Error: {ex.Message}"));
                    }
                }
            }

            if (fileDetails.Any())
            {
                isEffectivelyEmptyOverall = false;
            }
            else if (anyValidPathSpecEncountered && !issues.Any(i => i.IssueType == PathIssueType.PathIsEffectivelyEmpty))
            {
                isEffectivelyEmptyOverall = true;
            }
            return (fileDetails, issues, isEffectivelyEmptyOverall);
        }

        public async Task UpdateSyncStatusAsync(
            IEnumerable<AppConfig> apps,
            string destinationRoot,
            Action<AppConfig, AppSyncReport>? perAppStatusCallback = null)
        {
            await Task.Run(() =>
            {
                foreach (AppConfig app in apps)
                {
                    if (app == null) continue;
                    var report = new AppSyncReport();
                    try
                    {
                        var (sourceFiles, sourcePathIssues, sourceIsEffectivelyEmpty) =
                            GetPathContents(app.Paths, GetSpecialFolderRelativePath);
                        report.PathIssues.AddRange(sourcePathIssues);

                        var appDestPath = Path.Combine(destinationRoot, app.Id);

                        var (destFiles, destPathIssues, destIsEffectivelyEmpty) =
                            GetPathContents(new[] { appDestPath + Path.DirectorySeparatorChar },
                                            filePath => Path.GetRelativePath(appDestPath, filePath),
                                            appDestPath);

                        report.PathIssues.AddRange(destPathIssues.Select(issue => new PathIssue(
                            issue.PathSpec, issue.ExpandedPath, issue.IssueType, $"Backup location: {issue.Description}"
                        )));

                        bool canCompare = !report.PathIssues.Any(pi =>
                                            pi.IssueType == PathIssueType.PathSpecNullOrEmpty ||
                                            pi.IssueType == PathIssueType.PathUnexpandable ||
                                            (pi.IssueType == PathIssueType.PathInaccessible && pi.PathSpec == "N/A")
                                         );

                        if (canCompare)
                        {
                            foreach (var (relativePath, srcFileInfo) in sourceFiles)
                            {
                                if (destFiles.TryGetValue(relativePath, out var dstFileInfo))
                                {
                                    if (srcFileInfo.Length != dstFileInfo.Length || !AreFileTimesClose(srcFileInfo.LastWriteTimeUtc, dstFileInfo.LastWriteTimeUtc))
                                    {
                                        report.FileDifferences.Add(new FileDifference(relativePath, FileDifferenceType.ContentMismatch,
                                            $"Content differs. Source: {srcFileInfo.Length}b, {srcFileInfo.LastWriteTimeUtc:G}. Dest: {dstFileInfo.Length}b, {dstFileInfo.LastWriteTimeUtc:G}.",
                                            srcFileInfo, dstFileInfo));
                                    }
                                }
                                else
                                {
                                    report.FileDifferences.Add(new FileDifference(relativePath, FileDifferenceType.SourceOnly, "File exists only in source.", srcFileInfo));
                                }
                            }

                            foreach (var (relativePath, dstFileInfo) in destFiles)
                            {
                                if (!sourceFiles.ContainsKey(relativePath))
                                {
                                    report.FileDifferences.Add(new FileDifference(relativePath, FileDifferenceType.DestinationOnly, "File exists only in backup destination.", null, dstFileInfo));
                                }
                            }
                        }

                        report.DetermineOverallStatus();
                        perAppStatusCallback?.Invoke(app, report);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Critical error during UpdateSyncStatusAsync for app {AppId}", app?.Id);
                        report.OverallStatus = SyncStatus.Failed;
                        report.PathIssues.Add(new PathIssue("N/A", null, PathIssueType.OperationFailed, $"Operation failed due to critical error: {ex.Message}"));
                        if (app != null)
                        {
                            perAppStatusCallback?.Invoke(app, report);
                        }
                    }
                }
            });
        }

        public async Task BackupAsync(
            IEnumerable<AppConfig> configs,
            string destinationRoot,
            SyncMode mode,
            IProgress<int>? progress = null,
            Action<AppConfig, AppSyncReport>? perAppStatusCallback = null)
        {
            await Task.Run(async () =>
            {
                var appList = configs.ToList();
                int totalApps = appList.Count;
                if (totalApps == 0) { progress?.Report(100); return; }
                int processedApps = 0;

                foreach (var app in appList)
                {
                    if (app == null) { processedApps++; progress?.Report((int)(processedApps / (double)totalApps * 100)); continue; }

                    var report = new AppSyncReport { OverallStatus = SyncStatus.Syncing };
                    perAppStatusCallback?.Invoke(app, report);

                    var appDest = Path.Combine(destinationRoot, app.Id);
                    bool skipSyncDeletionDueToEmptySource = false;

                    try
                    {
                        var (sourceFiles, sourcePathIssues, sourceIsEffectivelyEmpty) =
                            GetPathContents(app.Paths, GetSpecialFolderRelativePath);
                        report.PathIssues.AddRange(sourcePathIssues);

                        bool criticalSourceIssue = sourcePathIssues.Any(pi =>
                            pi.IssueType == PathIssueType.PathSpecNullOrEmpty ||
                            pi.IssueType == PathIssueType.PathUnexpandable ||
                            pi.IssueType == PathIssueType.PathInaccessible);

                        if (criticalSourceIssue)
                        {
                            _logger.LogWarning("Critical source path problem for app {AppId}. Backup cannot proceed.", app.Id);
                            report.DetermineOverallStatus();
                            perAppStatusCallback?.Invoke(app, report);
                            processedApps++;
                            progress?.Report((int)(processedApps / (double)totalApps * 100));
                            continue;
                        }

                        if (!Directory.Exists(appDest))
                        {
                            try { Directory.CreateDirectory(appDest); }
                            catch (Exception ex)
                            {
                                report.PathIssues.Add(new PathIssue(appDest, appDest, PathIssueType.PathInaccessible, $"Failed to create backup destination directory: {ex.Message}"));
                                _logger.LogError(ex, "Failed to create backup destination directory for app {AppId}", app.Id);
                                report.DetermineOverallStatus();
                                perAppStatusCallback?.Invoke(app, report);
                                processedApps++;
                                progress?.Report((int)(processedApps / (double)totalApps * 100));
                                continue;
                            }
                        }

                        if (sourceIsEffectivelyEmpty && !sourceFiles.Any())
                        {
                            _logger.LogInformation("Source for app {AppId} is effectively empty or all specified source files have issues.", app.Id);
                            if (mode == SyncMode.Sync)
                            {
                                string msg = $"SYNC mode: Source for app {app.Id} is empty. Destination at {appDest} will NOT be cleared.";
                                _logger.LogWarning(msg);
                                report.PathIssues.Add(new PathIssue(appDest, appDest, PathIssueType.OperationPrevented, msg));
                                skipSyncDeletionDueToEmptySource = true;
                            }
                            // If not Sync, or if Sync but source is empty (and we skip deletion), report and continue.
                            // The main copy loop `foreach (var (relativePath, srcFileInfo) in sourceFiles)` will not run.
                            // The Sync deletion block will be skipped if skipSyncDeletionDueToEmptySource is true.
                            // So, just determine status and continue.
                            report.DetermineOverallStatus();
                            perAppStatusCallback?.Invoke(app, report);
                            processedApps++;
                            progress?.Report((int)(processedApps / (double)totalApps * 100));
                            continue;
                        }

                        var backedUpRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var (relativePath, srcFileInfo) in sourceFiles)
                        {
                            var destFile = Path.Combine(appDest, relativePath);
                            try
                            {
                                var destDir = Path.GetDirectoryName(destFile);
                                if (destDir != null && !Directory.Exists(destDir))
                                {
                                    Directory.CreateDirectory(destDir);
                                }
                            }
                            catch (Exception ex)
                            {
                                report.PathIssues.Add(new PathIssue(Path.GetDirectoryName(destFile), Path.GetDirectoryName(destFile), PathIssueType.PathInaccessible, $"Cannot create directory for {destFile}: {ex.Message}"));
                                _logger.LogError(ex, "Cannot create directory for {DestFile} in app {AppId}", destFile, app.Id);
                                report.FileDifferences.Add(new FileDifference(relativePath, FileDifferenceType.OperationFailed, $"Failed to create directory for destination file: {ex.Message}", srcFileInfo, null));
                                continue;
                            }

                            var destFileInfo = File.Exists(destFile) ? new FileInfo(destFile) : null;

                            if (destFileInfo == null ||
                                srcFileInfo.Length != destFileInfo.Length ||
                                !AreFileTimesClose(srcFileInfo.LastWriteTimeUtc, destFileInfo.LastWriteTimeUtc))
                            {
                                try
                                {
                                    await CopyFileAsync(srcFileInfo.FullName, destFile);
                                    File.SetLastWriteTimeUtc(destFile, srcFileInfo.LastWriteTimeUtc);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Error copying file {SourceFile} to {DestFile} for app {AppId}", srcFileInfo.FullName, destFile, app.Id);
                                    report.FileDifferences.Add(new FileDifference(relativePath, FileDifferenceType.OperationFailed, $"Failed to copy/update: {ex.Message}", srcFileInfo, destFileInfo));
                                }
                            }
                            if (!report.FileDifferences.Any(fd => fd.RelativePath == relativePath && fd.DifferenceType == FileDifferenceType.OperationFailed))
                            {
                                backedUpRelativePaths.Add(relativePath);
                            }
                        }

                        if (mode == SyncMode.Sync && !skipSyncDeletionDueToEmptySource)
                        {
                            var (destFilesForSync, destPathIssuesForSync, _) = GetPathContents(new[] { appDest + Path.DirectorySeparatorChar }, filePath => Path.GetRelativePath(appDest, filePath), appDest);
                            report.PathIssues.AddRange(destPathIssuesForSync.Select(issue => new PathIssue(issue.PathSpec, issue.ExpandedPath, issue.IssueType, $"SYNC Deletion Check (Backup Dest): {issue.Description}")));

                            bool criticalDestPathIssueForSync = destPathIssuesForSync.Any(pi =>
                                pi.IssueType == PathIssueType.PathSpecNullOrEmpty ||
                                pi.IssueType == PathIssueType.PathUnexpandable ||
                                pi.IssueType == PathIssueType.PathInaccessible);

                            if (criticalDestPathIssueForSync)
                            {
                                _logger.LogWarning("SYNC Backup: Critical issue reading backup destination paths for app {AppId}. Cannot perform deletions from backup.", app.Id);
                                report.PathIssues.Add(new PathIssue(appDest, appDest, PathIssueType.OperationPrevented, "SYNC Backup: Deletion step skipped due to critical issues accessing backup destination paths."));
                            }
                            else
                            {
                                foreach (var (relativeDestPath, destFileInfoForSync) in destFilesForSync)
                                {
                                    if (!backedUpRelativePaths.Contains(relativeDestPath))
                                    {
                                        try
                                        {
                                            _logger.LogInformation("SYNC Backup: Deleting {DestFileFullPath} as it's not in the source for app {AppId}", destFileInfoForSync.FullName, app.Id);
                                            await _retryPolicy.ExecuteAsync(() => Task.Run(() => File.Delete(destFileInfoForSync.FullName)));
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogError(ex, "Error deleting file {DestFileFullPath} during SYNC backup for app {AppId}", destFileInfoForSync.FullName, app.Id);
                                            report.FileDifferences.Add(new FileDifference(relativeDestPath, FileDifferenceType.OperationFailed, $"Failed to delete from destination (SYNC mode): {ex.Message}", null, destFileInfoForSync));
                                        }
                                    }
                                }
                                DeleteEmptyDirectories(appDest);
                            }
                        }
                        else if (mode == SyncMode.Sync && skipSyncDeletionDueToEmptySource)
                        {
                            _logger.LogInformation("SYNC Backup: Deletion from backup destination for app {AppId} was skipped because source was empty.", app.Id);
                        }
                        report.DetermineOverallStatus();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Critical error during BackupAsync for app {AppId}", app.Id);
                        report.OverallStatus = SyncStatus.Failed;
                        report.PathIssues.Add(new PathIssue("N/A", null, PathIssueType.OperationFailed, $"Operation failed due to critical error: {ex.Message}"));
                    }
                    finally
                    {
                        perAppStatusCallback?.Invoke(app, report);
                        processedApps++;
                        progress?.Report((int)(processedApps / (double)totalApps * 100));
                    }
                }
            });
        }

        public async Task RestoreAsync(
            IEnumerable<AppConfig> configs,
            string destinationRoot,
            SyncMode mode,
            IProgress<int>? progress = null,
            Action<AppConfig, AppSyncReport>? perAppStatusCallback = null)
        {
            await Task.Run(async () =>
            {
                var appList = configs.ToList();
                int totalApps = appList.Count;
                if (totalApps == 0) { progress?.Report(100); return; }
                int processedApps = 0;

                foreach (var app in appList)
                {
                    if (app == null) { processedApps++; progress?.Report((int)(processedApps / (double)totalApps * 100)); continue; }

                    var report = new AppSyncReport { OverallStatus = SyncStatus.Syncing };
                    perAppStatusCallback?.Invoke(app, report);

                    // Source for restore is the backup location for this app
                    var backupSourcePathForApp = Path.Combine(destinationRoot, app.Id);
                    bool skipSyncDeletionDueToEmptySource = false;

                    try
                    {
                        // 1. Get files from the backup location (these are the "source" for restore)
                        var (backupFiles, backupPathIssues, backupIsEffectivelyEmpty) =
                            GetPathContents(new[] { backupSourcePathForApp + Path.DirectorySeparatorChar },
                                            filePath => Path.GetRelativePath(backupSourcePathForApp, filePath),
                                            backupSourcePathForApp);
                        report.PathIssues.AddRange(backupPathIssues.Select(pi =>
                            new PathIssue(pi.PathSpec, pi.ExpandedPath, pi.IssueType, $"Restore Source (Backup Location): {pi.Description}")));

                        bool criticalBackupSourceIssue = backupPathIssues.Any(pi =>
                            pi.IssueType == PathIssueType.PathSpecNullOrEmpty ||
                            pi.IssueType == PathIssueType.PathUnexpandable ||
                            pi.IssueType == PathIssueType.PathNotFound || // Backup for app ID not found
                            pi.IssueType == PathIssueType.PathInaccessible); // Backup for app ID inaccessible

                        if (criticalBackupSourceIssue)
                        {
                            _logger.LogWarning("Critical problem with backup source for app {AppId} at {BackupPath}. Restore cannot proceed.", app.Id, backupSourcePathForApp);
                            report.DetermineOverallStatus();
                            perAppStatusCallback?.Invoke(app, report);
                            processedApps++;
                            progress?.Report((int)(processedApps / (double)totalApps * 100));
                            continue;
                        }

                        if (backupIsEffectivelyEmpty && !backupFiles.Any())
                        {
                            _logger.LogInformation("Backup source for app {AppId} is effectively empty. Nothing to restore.", app.Id);
                            if (mode == SyncMode.Sync)
                            {
                                string appPathsSummary = app.Paths.Count > 0 ? string.Join(", ", app.Paths.Take(2)) + (app.Paths.Count > 2 ? "..." : "") : "N/A";
                                string msg = $"SYNC mode: Backup source for app {app.Id} is empty. Original application files at '{appPathsSummary}' will NOT be cleared, mirroring backup behavior.";
                                _logger.LogWarning(msg);
                                report.PathIssues.Add(new PathIssue(appPathsSummary, null, PathIssueType.OperationPrevented, msg));
                                skipSyncDeletionDueToEmptySource = true;
                            }
                        }

                        var restoredRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (backupFiles.Any())
                        {
                            foreach (var (relativePath, backupFileInfo) in backupFiles)
                            {
                                string? restoreDestFullPath = null;
                                try
                                {
                                    restoreDestFullPath = SpecialFolderUtil.ExpandSpecialFolders(relativePath);
                                    if (string.IsNullOrWhiteSpace(restoreDestFullPath))
                                    {
                                        string errMsg = $"Could not expand restore destination path for relative path '{relativePath}'.";
                                        _logger.LogWarning(errMsg);
                                        report.PathIssues.Add(new PathIssue(relativePath, null, PathIssueType.PathUnexpandable, errMsg));
                                        report.FileDifferences.Add(new FileDifference(relativePath, FileDifferenceType.OperationFailed, errMsg, backupFileInfo, null));
                                        continue;
                                    }

                                    var destDir = Path.GetDirectoryName(restoreDestFullPath);
                                    if (destDir != null && !Directory.Exists(destDir))
                                    {
                                        Directory.CreateDirectory(destDir);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    string errMsg = $"Failed to prepare destination directory for '{restoreDestFullPath ?? relativePath}': {ex.Message}";
                                    _logger.LogError(ex, "Error preparing destination for app {AppId}, relative path {RelativePath}", app.Id, relativePath);
                                    report.PathIssues.Add(new PathIssue(relativePath, restoreDestFullPath, PathIssueType.PathInaccessible, errMsg));
                                    report.FileDifferences.Add(new FileDifference(relativePath, FileDifferenceType.OperationFailed, $"Failed to create/access destination directory: {ex.Message}", backupFileInfo, null));
                                    continue;
                                }

                                var originalFileInfo = File.Exists(restoreDestFullPath) ? new FileInfo(restoreDestFullPath) : null;

                                if (originalFileInfo == null ||
                                    backupFileInfo.Length != originalFileInfo.Length ||
                                    !AreFileTimesClose(backupFileInfo.LastWriteTimeUtc, originalFileInfo.LastWriteTimeUtc))
                                {
                                    try
                                    {
                                        await CopyFileAsync(backupFileInfo.FullName, restoreDestFullPath);
                                        File.SetLastWriteTimeUtc(restoreDestFullPath, backupFileInfo.LastWriteTimeUtc);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Error restoring file {BackupFile} to {RestoreDestFile} for app {AppId}", backupFileInfo.FullName, restoreDestFullPath, app.Id);
                                        report.FileDifferences.Add(new FileDifference(relativePath, FileDifferenceType.OperationFailed, $"Failed to copy/update: {ex.Message}", backupFileInfo, originalFileInfo));
                                    }
                                }
                                if (!report.FileDifferences.Any(fd => fd.RelativePath == relativePath && fd.DifferenceType == FileDifferenceType.OperationFailed))
                                {
                                    restoredRelativePaths.Add(relativePath);
                                }
                            }
                        }

                        if (mode == SyncMode.Sync && !skipSyncDeletionDueToEmptySource)
                        {
                            var (currentAppFiles, currentAppPathIssues, _) =
                                GetPathContents(app.Paths, GetSpecialFolderRelativePath);
                            report.PathIssues.AddRange(currentAppPathIssues.Select(pi =>
                                new PathIssue(pi.PathSpec, pi.ExpandedPath, pi.IssueType, $"Restore Destination Sync Check (App Location): {pi.Description}")));

                            bool criticalAppPathIssueForSync = currentAppPathIssues.Any(pi =>
                                pi.IssueType == PathIssueType.PathSpecNullOrEmpty ||
                                pi.IssueType == PathIssueType.PathUnexpandable ||
                                pi.IssueType == PathIssueType.PathInaccessible);

                            if (criticalAppPathIssueForSync)
                            {
                                _logger.LogWarning("SYNC Restore: Critical issue reading original application paths for app {AppId}. Cannot perform deletions.", app.Id);
                                report.PathIssues.Add(new PathIssue(string.Join(";", app.Paths.Take(3)) + (app.Paths.Count > 3 ? "..." : ""), null, PathIssueType.OperationPrevented, "SYNC Restore: Deletion step skipped due to critical issues accessing original application paths."));
                            }
                            else
                            {
                                foreach (var (liveRelativePath, liveFileInfo) in currentAppFiles)
                                {
                                    if (!restoredRelativePaths.Contains(liveRelativePath))
                                    {
                                        bool restoreAttemptedAndFailed = report.FileDifferences.Any(fd =>
                                            fd.RelativePath == liveRelativePath &&
                                            fd.DifferenceType == FileDifferenceType.OperationFailed &&
                                            fd.SourceFileInfo != null); // SourceFileInfo indicates it was from backup attempt

                                        if (!restoreAttemptedAndFailed)
                                        {
                                            try
                                            {
                                                _logger.LogInformation("SYNC Restore: Deleting {AppFileFullPath} as it's not in the backup (or wasn't restored) for app {AppId}", liveFileInfo.FullName, app.Id);
                                                await _retryPolicy.ExecuteAsync(() => Task.Run(() => File.Delete(liveFileInfo.FullName)));
                                                // report.FileDifferences.Add(new FileDifference(liveRelativePath, FileDifferenceType.DestinationOnly, "Deleted from application location (SYNC mode, not in backup).", null, liveFileInfo));
                                            }
                                            catch (Exception ex)
                                            {
                                                _logger.LogError(ex, "Error deleting file {AppFileFullPath} during SYNC restore for app {AppId}", liveFileInfo.FullName, app.Id);
                                                report.FileDifferences.Add(new FileDifference(liveRelativePath, FileDifferenceType.OperationFailed, $"Failed to delete from application location (SYNC mode): {ex.Message}", null, liveFileInfo));
                                            }
                                        }
                                    }
                                }

                                foreach (var pathSpecEntry in app.Paths)
                                {
                                    if (string.IsNullOrWhiteSpace(pathSpecEntry)) continue;
                                    var expandedPath = SpecialFolderUtil.ExpandSpecialFolders(pathSpecEntry);
                                    if (string.IsNullOrWhiteSpace(expandedPath)) continue;

                                    bool isLikelyDirectorySpec = pathSpecEntry.EndsWith(Path.DirectorySeparatorChar.ToString()) ||
                                                               pathSpecEntry.EndsWith(Path.AltDirectorySeparatorChar.ToString());

                                    string pathToClean = isLikelyDirectorySpec ? expandedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : expandedPath;

                                    if (Directory.Exists(pathToClean))
                                    {
                                        DeleteEmptyDirectories(pathToClean);
                                    }
                                }
                            }
                        }
                        else if (mode == SyncMode.Sync && skipSyncDeletionDueToEmptySource)
                        {
                            _logger.LogInformation("SYNC Restore: Deletion from app location for app {AppId} was skipped because backup source was empty.", app.Id);
                        }
                        report.DetermineOverallStatus();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Critical error during RestoreAsync for app {AppId}", app.Id);
                        report.OverallStatus = SyncStatus.Failed;
                        report.PathIssues.Add(new PathIssue("N/A", null, PathIssueType.OperationFailed, $"Operation failed due to critical error: {ex.Message}"));
                    }
                    finally
                    {
                        perAppStatusCallback?.Invoke(app, report);
                        processedApps++;
                        progress?.Report((int)(processedApps / (double)totalApps * 100));
                    }
                }
            });
        }

        private readonly struct ExpandedPathInfo(string original, string expanded, bool isDir)
        {
            public string Original { get; } = original;
            public string Expanded { get; } = expanded;
            public bool IsDir { get; } = isDir;
        }

        private static string GetDriveLetterRelativePath(string fullPath)
        {
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root) || root.Length < 2 || !Path.IsPathRooted(fullPath) || root.Length > 3)
                return fullPath;
            var driveLetter = root[0].ToString();
            var rest = fullPath.Substring(root.Length);
            return Path.Combine(driveLetter, rest);
        }

        private static string GetSpecialFolderRelativePath(string fullPath)
        {
            var specialPath = SpecialFolderUtil.ConvertToSpecialFolderPath(fullPath);
            if (specialPath == fullPath && Path.IsPathRooted(fullPath))
            {
                return GetDriveLetterRelativePath(fullPath);
            }
            return specialPath;
        }

        private static async Task CopyFileAsync(string sourceFile, string destFile)
        {
            const int bufferSize = 81920;
            await _retryPolicy.ExecuteAsync(async () =>
            {
                using var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
                using var destStream = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
                await sourceStream.CopyToAsync(destStream);
            });
        }

        private void DeleteEmptyDirectories(string rootDirectory)
        {
            if (!Directory.Exists(rootDirectory)) return;
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(rootDirectory, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length)) // Process deepest first
                {
                    // Check if directory is empty (of files and subdirectories)
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        try
                        {
                            Directory.Delete(dir);
                            _logger.LogInformation("Deleted empty directory {Directory}", dir);
                        }
                        catch (IOException ex) { _logger.LogWarning(ex, "Could not delete empty directory {Directory} (possibly in use or timing issue).", dir); }
                        catch (UnauthorizedAccessException ex) { _logger.LogWarning(ex, "Access denied deleting empty directory {Directory}.", dir); }
                    }
                }
            }
            catch (Exception ex) // Catch broader exceptions for the enumeration itself
            {
                _logger.LogError(ex, "Error during DeleteEmptyDirectories for {rootDirectory}", rootDirectory);
            }
        }
    }
}