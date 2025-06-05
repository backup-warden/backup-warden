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
                // If we had valid path specs, but no files were found, and no "PathIsEffectivelyEmpty" issue was logged for a directory,
                // it implies all valid specs were for individual files that might have had issues (NotFound, Inaccessible).
                // In this specific sub-case, if there are no files, it's empty.
                // However, the PathIsEffectivelyEmpty for directories already covers the main scenario.
                // This primarily ensures that if all pathSpecs point to non-existent/inaccessible *files*, it's still empty.
                isEffectivelyEmptyOverall = true;
            }
            // If !anyValidPathSpecEncountered, it means all pathSpecs were bad (null/unexpandable), so it's effectively empty.
            // isEffectivelyEmptyOverall is initialized to true, so this holds.

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

                        // Perform comparison only if there are no critical issues that prevent comparison
                        bool canCompare = !report.PathIssues.Any(pi =>
                                            pi.IssueType == PathIssueType.PathSpecNullOrEmpty ||
                                            pi.IssueType == PathIssueType.PathUnexpandable ||
                                            (pi.IssueType == PathIssueType.PathInaccessible && pi.PathSpec == "N/A") // Global issue
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

                    try
                    {
                        var (sourceFiles, sourcePathIssues, sourceIsEffectivelyEmpty) =
                            GetPathContents(app.Paths, GetSpecialFolderRelativePath);
                        report.PathIssues.AddRange(sourcePathIssues);

                        bool criticalSourceIssue = sourcePathIssues.Any(pi =>
                            pi.IssueType == PathIssueType.PathSpecNullOrEmpty ||
                            pi.IssueType == PathIssueType.PathUnexpandable ||
                            pi.IssueType == PathIssueType.PathInaccessible); // Inaccessible source is critical for backup

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
                            // This is now primarily a warning, logged in GetPathContents if a dir is empty.
                            // The main check here is if there are *any* files to process.
                            _logger.LogInformation("Source for app {AppId} is effectively empty or all specified source files have issues.", app.Id);
                            if (mode == SyncMode.Sync)
                            {
                                string msg = $"SYNC mode: Source for app {app.Id} is empty. Destination at {appDest} will NOT be cleared.";
                                _logger.LogWarning(msg);
                                report.PathIssues.Add(new PathIssue(appDest, appDest, PathIssueType.OperationPrevented, msg));
                            }
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
                            try { Directory.CreateDirectory(Path.GetDirectoryName(destFile)!); }
                            catch (Exception ex)
                            {
                                report.PathIssues.Add(new PathIssue(Path.GetDirectoryName(destFile), Path.GetDirectoryName(destFile), PathIssueType.PathInaccessible, $"Cannot create directory for {destFile}: {ex.Message}"));
                                _logger.LogError(ex, "Cannot create directory for {DestFile} in app {AppId}", destFile, app.Id);
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
                                backedUpRelativePaths.Add(relativePath); // Only add if copy didn't fail
                            }
                        }

                        if (mode == SyncMode.Sync)
                        {
                            var (destFilesForSync, destPathIssuesForSync, _) = GetPathContents(new[] { appDest + Path.DirectorySeparatorChar }, filePath => Path.GetRelativePath(appDest, filePath), appDest);
                            report.PathIssues.AddRange(destPathIssuesForSync.Select(issue => new PathIssue(issue.PathSpec, issue.ExpandedPath, issue.IssueType, $"SYNC Deletion Check (Backup Dest): {issue.Description}")));

                            foreach (var (relativeDestPath, destFileInfoForSync) in destFilesForSync)
                            {
                                if (!backedUpRelativePaths.Contains(relativeDestPath))
                                {
                                    try
                                    {
                                        _logger.LogInformation("SYNC Backup: Deleting {DestFileFullPath} as it's not in the source for app {AppId}", destFileInfoForSync.FullName, app.Id);
                                        await _retryPolicy.ExecuteAsync(() => Task.Run(() => File.Delete(destFileInfoForSync.FullName)));
                                        // Optionally report this deletion as a "difference" or an "action"
                                        // report.FileDifferences.Add(new FileDifference(relativeDestPath, FileDifferenceType.DestinationOnly, "Deleted from destination (SYNC mode).", null, destFileInfoForSync));
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

                    var appBackupSourceRoot = Path.Combine(destinationRoot, app.Id);

                    try
                    {
                        // Validate AppConfig.Paths (live destination paths for restore)
                        var (livePathTargets, livePathIssues, _) = GetPathContents(app.Paths, GetSpecialFolderRelativePath);
                        report.PathIssues.AddRange(livePathIssues.Select(issue => new PathIssue(issue.PathSpec, issue.ExpandedPath, issue.IssueType, $"Live Target Path: {issue.Description}")));

                        bool criticalLivePathIssue = livePathIssues.Any(pi =>
                            pi.IssueType == PathIssueType.PathSpecNullOrEmpty ||
                            pi.IssueType == PathIssueType.PathUnexpandable ||
                            pi.IssueType == PathIssueType.PathInaccessible);

                        if (criticalLivePathIssue || !app.Paths.Any())
                        {
                            _logger.LogWarning("Critical issue with configured live target paths for app {AppId}, or no paths configured. Restore cannot proceed.", app.Id);
                            if (!app.Paths.Any()) report.PathIssues.Add(new PathIssue("AppConfig.Paths", null, PathIssueType.PathSpecNullOrEmpty, "No live target paths configured for restore."));
                            report.DetermineOverallStatus();
                            perAppStatusCallback?.Invoke(app, report);
                            processedApps++;
                            progress?.Report((int)(processedApps / (double)totalApps * 100));
                            continue;
                        }

                        // Expanded live target paths for validation during restore
                        var expandedAppTargetBasePaths = app.Paths
                            .Select(p =>
                            {
                                var expanded = SpecialFolderUtil.ExpandSpecialFolders(p);
                                if (string.IsNullOrWhiteSpace(expanded)) return (ExpandedPathInfo?)null;
                                bool isDir = expanded.EndsWith(Path.DirectorySeparatorChar.ToString()) || expanded.EndsWith(Path.AltDirectorySeparatorChar.ToString());
                                return new ExpandedPathInfo(p, Path.GetFullPath(isDir ? expanded.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : expanded), isDir);
                            })
                            .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Value.Expanded))
                            .Select(p => p!.Value)
                            .ToList();
                        if (!expandedAppTargetBasePaths.Any()) // Should be caught by criticalLivePathIssue if paths were bad
                        {
                            _logger.LogWarning("No valid live target paths could be determined for app {AppId} after expansion. Restore cannot proceed.", app.Id);
                            report.PathIssues.Add(new PathIssue("AppConfig.Paths", string.Join(";", app.Paths), PathIssueType.PathUnexpandable, "All configured live target paths are invalid or unexpandable."));
                            report.DetermineOverallStatus();
                            perAppStatusCallback?.Invoke(app, report);
                            processedApps++;
                            progress?.Report((int)(processedApps / (double)totalApps * 100));
                            continue;
                        }


                        // Get files from backup source
                        var (backupFiles, backupPathIssues, backupIsEffectivelyEmpty) =
                            GetPathContents(new[] { appBackupSourceRoot + Path.DirectorySeparatorChar },
                                filePath => Path.GetRelativePath(appBackupSourceRoot, filePath), appBackupSourceRoot);
                        report.PathIssues.AddRange(backupPathIssues.Select(issue => new PathIssue(issue.PathSpec, issue.ExpandedPath, issue.IssueType, $"Backup Source: {issue.Description}")));

                        bool criticalBackupSourceIssue = backupPathIssues.Any(pi => pi.IssueType == PathIssueType.PathInaccessible); // Inaccessible backup source is critical

                        if (criticalBackupSourceIssue)
                        {
                            _logger.LogWarning("Critical issue reading backup source for app {AppId}. Restore cannot proceed.", app.Id);
                            report.DetermineOverallStatus();
                            perAppStatusCallback?.Invoke(app, report);
                            processedApps++;
                            progress?.Report((int)(processedApps / (double)totalApps * 100));
                            continue;
                        }

                        if (backupIsEffectivelyEmpty && !backupFiles.Any())
                        {
                            _logger.LogInformation("Backup source for app {AppId} is effectively empty.", app.Id);
                            report.PathIssues.Add(new PathIssue(appBackupSourceRoot, appBackupSourceRoot, PathIssueType.PathIsEffectivelyEmpty, "Backup source is empty or contains no files."));
                            if (mode == SyncMode.Sync)
                            {
                                string msg = $"SYNC mode: Backup source for app {app.Id} is empty. Live application paths will NOT be cleared.";
                                _logger.LogWarning(msg);
                                report.PathIssues.Add(new PathIssue("Live Paths", string.Join(";", app.Paths), PathIssueType.OperationPrevented, msg));
                            }
                            report.DetermineOverallStatus();
                            perAppStatusCallback?.Invoke(app, report);
                            processedApps++;
                            progress?.Report((int)(processedApps / (double)totalApps * 100));
                            continue;
                        }

                        var restoredLiveFileFullPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var (relativePathFromBackupAppRoot, backupFileInfo) in backupFiles)
                        {
                            var pathAfterUserMapping = SpecialFolderUtil.MapBackupUserPathToCurrentUser(relativePathFromBackupAppRoot);
                            var finalLivePathCandidate = SpecialFolderUtil.ExpandSpecialFolders(pathAfterUserMapping);

                            if (string.IsNullOrWhiteSpace(finalLivePathCandidate))
                            {
                                report.PathIssues.Add(new PathIssue(relativePathFromBackupAppRoot, null, PathIssueType.PathUnexpandable, $"Could not determine live path for backup item '{relativePathFromBackupAppRoot}'."));
                                continue;
                            }
                            var finalLiveFullPath = Path.GetFullPath(finalLivePathCandidate);

                            bool isInConfiguredPath = expandedAppTargetBasePaths.Any(etp =>
                                etp.IsDir
                                    ? finalLiveFullPath.StartsWith(etp.Expanded + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || finalLiveFullPath.Equals(etp.Expanded, StringComparison.OrdinalIgnoreCase)
                                    : finalLiveFullPath.Equals(etp.Expanded, StringComparison.OrdinalIgnoreCase));

                            if (!isInConfiguredPath)
                            {
                                _logger.LogDebug("Skipping restore of backup file {RelativePath} to {FinalLiveFullPath} as it's not under a configured target path for app {AppId}", relativePathFromBackupAppRoot, finalLiveFullPath, app.Id);
                                report.PathIssues.Add(new PathIssue(finalLiveFullPath, finalLiveFullPath, PathIssueType.OperationPrevented, $"File '{relativePathFromBackupAppRoot}' maps to '{finalLiveFullPath}', which is outside configured live restore paths. Skipped."));
                                continue;
                            }

                            try { Directory.CreateDirectory(Path.GetDirectoryName(finalLiveFullPath)!); }
                            catch (Exception ex)
                            {
                                report.PathIssues.Add(new PathIssue(Path.GetDirectoryName(finalLiveFullPath), Path.GetDirectoryName(finalLiveFullPath), PathIssueType.PathInaccessible, $"Cannot create live directory for {finalLiveFullPath}: {ex.Message}"));
                                _logger.LogError(ex, "Cannot create live directory for {FinalLiveFullPath} in app {AppId}", finalLiveFullPath, app.Id);
                                continue;
                            }

                            try
                            {
                                await RestoreFileInternalAsync(backupFileInfo.FullName, finalLiveFullPath);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error restoring file {BackupFile} to {LiveFile} for app {AppId}", backupFileInfo.FullName, finalLiveFullPath, app.Id);
                                report.FileDifferences.Add(new FileDifference(relativePathFromBackupAppRoot, FileDifferenceType.OperationFailed, $"Failed to restore to live path: {ex.Message}", backupFileInfo));
                            }
                            if (!report.FileDifferences.Any(fd => fd.RelativePath == relativePathFromBackupAppRoot && fd.DifferenceType == FileDifferenceType.OperationFailed))
                            {
                                restoredLiveFileFullPaths.Add(finalLiveFullPath);
                            }
                        }

                        if (mode == SyncMode.Sync)
                        {
                            foreach (var targetPathInfo in expandedAppTargetBasePaths)
                            {
                                string currentLivePathTarget = targetPathInfo.Expanded;
                                if (!Directory.Exists(currentLivePathTarget) && !File.Exists(currentLivePathTarget)) continue; // Nothing to delete if target doesn't exist

                                var (liveFilesToCheck, livePathIssuesForSync, _) = GetPathContents(
                                    new[] { targetPathInfo.IsDir ? currentLivePathTarget + Path.DirectorySeparatorChar : currentLivePathTarget },
                                    filePath => Path.GetFullPath(filePath),
                                    targetPathInfo.IsDir ? currentLivePathTarget : Path.GetDirectoryName(currentLivePathTarget)
                                );
                                report.PathIssues.AddRange(livePathIssuesForSync.Select(issue => new PathIssue(issue.PathSpec, issue.ExpandedPath, issue.IssueType, $"SYNC Deletion Check (Live Path): {issue.Description}")));

                                foreach (var (liveFileFullPath, liveFileInfo) in liveFilesToCheck)
                                {
                                    if (!restoredLiveFileFullPaths.Contains(liveFileFullPath))
                                    {
                                        try
                                        {
                                            _logger.LogInformation("SYNC Restore: Deleting {LiveFileFullPath} from app {AppId} as it's not in the current backup set being restored.", liveFileFullPath, app.Id);
                                            await _retryPolicy.ExecuteAsync(() => Task.Run(() => File.Delete(liveFileFullPath)));
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogError(ex, "Error deleting live file {LiveFileFullPath} during SYNC restore for app {AppId}", liveFileFullPath, app.Id);
                                            report.FileDifferences.Add(new FileDifference(Path.GetRelativePath(targetPathInfo.Expanded, liveFileFullPath), FileDifferenceType.OperationFailed, $"Failed to delete from live path (SYNC mode): {ex.Message}", null, liveFileInfo));
                                        }
                                    }
                                }
                                if (targetPathInfo.IsDir) DeleteEmptyDirectories(currentLivePathTarget);
                            }
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

        private async Task RestoreFileInternalAsync(string backupFile, string destFile)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            if (File.Exists(backupFile))
            {
                await CopyFileAsync(backupFile, destFile);
                var backupInfo = new FileInfo(backupFile);
                try { File.SetLastWriteTimeUtc(destFile, backupInfo.LastWriteTimeUtc); }
                catch (IOException ex) { _logger.LogError(ex, "Could not set LastWriteTimeUtc for {destFile}", destFile); }
            }
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
                    .OrderByDescending(d => d.Length))
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        try { Directory.Delete(dir); }
                        catch (IOException ex) { _logger.LogError(ex, "Could not delete empty directory {Directory}", dir); }
                        catch (UnauthorizedAccessException ex) { _logger.LogError(ex, "Access denied deleting empty directory {Directory}", dir); }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during DeleteEmptyDirectories for {rootDirectory}", rootDirectory);
            }
        }
    }
}