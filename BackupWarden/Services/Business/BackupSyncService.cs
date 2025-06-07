using BackupWarden.Models;
using BackupWarden.Utils;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BackupWarden.Models.Extensions;

namespace BackupWarden.Services.Business
{
    public delegate void AppStatusUpdateCallback(AppConfig app, SyncStatus status, string summaryReport, string detailedReport);

    public interface IBackupSyncService
    {
        Task UpdateSyncStatusAsync(IEnumerable<AppConfig> apps, string backupRoot, AppStatusUpdateCallback? perAppStatusCallback = null);
        Task RestoreAsync(IEnumerable<AppConfig> configs, string backupRoot, SyncMode mode, IProgress<int>? progress = null, AppStatusUpdateCallback? perAppStatusCallback = null);
        Task BackupAsync(IEnumerable<AppConfig> configs, string backupRoot, SyncMode mode, IProgress<int>? progress = null, AppStatusUpdateCallback? perAppStatusCallback = null);
    }

    public class BackupSyncService : IBackupSyncService
    {
        private readonly ILogger<BackupSyncService> _logger;
        private readonly IFileSystemOperations _fileSystemOperations;

        public BackupSyncService(ILogger<BackupSyncService> logger, IFileSystemOperations fileSystemOperations)
        {
            _logger = logger;
            _fileSystemOperations = fileSystemOperations;
        }

        private static bool AreFileTimesClose(DateTime t1, DateTime t2, TimeSpan? tolerance = null)
        {
            tolerance ??= TimeSpan.FromSeconds(2);
            return (t1 > t2) ? (t1 - t2 <= tolerance) : (t2 - t1 <= tolerance);
        }

        private (Dictionary<string, FileInfo> FilesByRelativePath, List<PathIssue> Issues, bool IsEffectivelyEmptyOverall) GetPathContents(
            IEnumerable<string> pathSpecs,
            Func<string, string> getRelativePathFunc,
            PathIssueSource issueSource,
            string? baseDirectoryForRelativePath = null)
        {
            var fileDetails = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            var issues = new List<PathIssue>();
            bool isEffectivelyEmptyOverall = true;
            bool anyValidPathSpecEncountered = false;

            if (pathSpecs == null || !pathSpecs.Any())
            {
                issues.Add(new PathIssue("N/A", null, PathIssueType.PathSpecNullOrEmpty, issueSource, "No path specifications provided."));
                return (fileDetails, issues, true);
            }

            foreach (var pathSpec in pathSpecs)
            {
                if (string.IsNullOrWhiteSpace(pathSpec))
                {
                    issues.Add(new PathIssue(pathSpec ?? "N/A", null, PathIssueType.PathSpecNullOrEmpty, issueSource, "Path specification is null or whitespace."));
                    continue;
                }

                var expandedPath = SpecialFolderUtil.ExpandSpecialFolders(pathSpec);
                if (string.IsNullOrWhiteSpace(expandedPath))
                {
                    issues.Add(new PathIssue(pathSpec, null, PathIssueType.PathUnexpandable, issueSource, $"Path specification '{pathSpec}' could not be expanded."));
                    continue;
                }

                anyValidPathSpecEncountered = true;
                bool isOriginalPathSpecDirectory = pathSpec.EndsWith(Path.DirectorySeparatorChar.ToString()) ||
                                                   pathSpec.EndsWith(Path.AltDirectorySeparatorChar.ToString());

                var actualPath = isOriginalPathSpecDirectory ? expandedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : expandedPath;

                if (isOriginalPathSpecDirectory)
                {
                    if (!_fileSystemOperations.DirectoryExists(actualPath))
                    {
                        issues.Add(new PathIssue(pathSpec, actualPath, PathIssueType.PathNotFound, issueSource, $"Directory '{actualPath}' (from '{pathSpec}') not found."));
                        continue;
                    }
                    try
                    {
                        var filesInDir = _fileSystemOperations.EnumerateFiles(actualPath, "*", SearchOption.AllDirectories).ToList();
                        if (filesInDir.Count == 0)
                        {
                            issues.Add(new PathIssue(pathSpec, actualPath, PathIssueType.PathIsEffectivelyEmpty, issueSource, $"Directory '{actualPath}' (from '{pathSpec}') is empty."));
                        }

                        foreach (var file in filesInDir)
                        {
                            var fi = _fileSystemOperations.GetFileInfo(file);
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
                        issues.Add(new PathIssue(pathSpec, actualPath, PathIssueType.PathInaccessible, issueSource, $"Access denied to directory '{actualPath}' (from '{pathSpec}'). Error: {ex.Message}"));
                    }
                    catch (DirectoryNotFoundException ex)
                    {
                        issues.Add(new PathIssue(pathSpec, actualPath, PathIssueType.PathNotFound, issueSource, $"Directory '{actualPath}' (from '{pathSpec}') not found during enumeration. Error: {ex.Message}"));
                    }
                    catch (Exception ex)
                    {
                        issues.Add(new PathIssue(pathSpec, actualPath, PathIssueType.PathInaccessible, issueSource, $"Error enumerating directory '{actualPath}' (from '{pathSpec}'). Error: {ex.Message}"));
                    }
                }
                else
                {
                    if (!_fileSystemOperations.FileExists(actualPath))
                    {
                        issues.Add(new PathIssue(pathSpec, actualPath, PathIssueType.PathNotFound, issueSource, $"File '{actualPath}' (from '{pathSpec}') not found."));
                        continue;
                    }
                    try
                    {
                        var fi = _fileSystemOperations.GetFileInfo(actualPath);
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
                        issues.Add(new PathIssue(pathSpec, actualPath, PathIssueType.PathInaccessible, issueSource, $"Access denied to file '{actualPath}' (from '{pathSpec}'). Error: {ex.Message}"));
                    }
                    catch (FileNotFoundException ex)
                    {
                        issues.Add(new PathIssue(pathSpec, actualPath, PathIssueType.PathNotFound, issueSource, $"File '{actualPath}' (from '{pathSpec}') not found during info retrieval. Error: {ex.Message}"));
                    }
                    catch (Exception ex)
                    {
                        issues.Add(new PathIssue(pathSpec, actualPath, PathIssueType.PathInaccessible, issueSource, $"Error accessing file '{actualPath}' (from '{pathSpec}'). Error: {ex.Message}"));
                    }
                }
            }

            if (fileDetails.Count != 0)
            {
                isEffectivelyEmptyOverall = false;
            }
            else if (anyValidPathSpecEncountered && !issues.Any(i => i.IssueType == PathIssueType.PathIsEffectivelyEmpty && fileDetails.Count == 0))
            {
                isEffectivelyEmptyOverall = true;
            }
            else if (!anyValidPathSpecEncountered)
            {
                isEffectivelyEmptyOverall = true;
            }

            return (fileDetails, issues, isEffectivelyEmptyOverall);
        }

        private void ProcessSingleApp(
            AppConfig app,
            string backupRoot,
            AppStatusUpdateCallback? perAppStatusCallback,
            Action<AppConfig, AppSyncReport, string> appSpecificOperation)
        {
            if (app == null) return;

            var report = new AppSyncReport { OverallStatus = SyncStatus.Syncing };
            app.LastSyncReport = report;
            var appBackupRootPath = Path.Combine(backupRoot, app.Id);
            report.AppBackupRootPath = appBackupRootPath + Path.DirectorySeparatorChar;

            perAppStatusCallback?.Invoke(app, report.OverallStatus, report.ToSummaryReport(), report.ToDetailedReport());

            try
            {
                appSpecificOperation(app, report, appBackupRootPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error during operation for app {AppId}", app.Id);
                report.OverallStatus = SyncStatus.Failed;
                report.PathIssues.Add(new PathIssue("N/A", null, PathIssueType.OperationFailed, PathIssueSource.Operation, $"Operation failed due to critical error: {ex.Message}"));
            }
            finally
            {
                if (report.OverallStatus == SyncStatus.Syncing)
                {
                    report.UpdateOverallStatus();
                }
                perAppStatusCallback?.Invoke(app, report.OverallStatus, report.ToSummaryReport(), report.ToDetailedReport());
            }
        }

        public async Task UpdateSyncStatusAsync(
            IEnumerable<AppConfig> apps,
            string backupRoot,
            AppStatusUpdateCallback? perAppStatusCallback = null)
        {
            await Task.Run(() =>
            {
                foreach (AppConfig app in apps)
                {
                    ProcessSingleApp(app, backupRoot, perAppStatusCallback, (currentApp, report, appDestPath) =>
                    {
                        var (sourceFiles, sourcePathIssues, _) =
                            GetPathContents(currentApp.Paths, GetSpecialFolderRelativePath, PathIssueSource.Application);
                        report.PathIssues.AddRange(sourcePathIssues);

                        var (destFiles, destPathIssues, _) =
                            GetPathContents([report.AppBackupRootPath],
                                            filePath => Path.GetRelativePath(appDestPath, filePath),
                                            PathIssueSource.BackupLocation,
                                            appDestPath);
                        report.PathIssues.AddRange(destPathIssues);

                        bool appSourcePathIssue = sourcePathIssues.Any(pi =>
                            pi.IssueType == PathIssueType.PathSpecNullOrEmpty ||
                            pi.IssueType == PathIssueType.PathUnexpandable ||
                            pi.IssueType == PathIssueType.PathInaccessible);

                        bool backupLocationIssue = destPathIssues.Any(pi =>
                            pi.PathSpec == report.AppBackupRootPath &&
                            (pi.IssueType == PathIssueType.PathUnexpandable || pi.IssueType == PathIssueType.PathInaccessible));

                        bool canCompare = !appSourcePathIssue && !backupLocationIssue;

                        if (canCompare)
                        {
                            foreach (var (relativePath, appFileInfo) in sourceFiles)
                            {
                                if (destFiles.TryGetValue(relativePath, out var backupFileInfo))
                                {
                                    if (appFileInfo.Length != backupFileInfo.Length || !AreFileTimesClose(appFileInfo.LastWriteTimeUtc, backupFileInfo.LastWriteTimeUtc))
                                    {
                                        report.FileDifferences.Add(new FileDifference(relativePath, FileDifferenceType.ContentMismatch,
                                            $"Content differs. App: {appFileInfo.Length}b, {appFileInfo.LastWriteTimeUtc:G}. Backup: {backupFileInfo.Length}b, {backupFileInfo.LastWriteTimeUtc:G}.",
                                            applicationFileInfo: appFileInfo, backupFileInfo: backupFileInfo));
                                    }
                                }
                                else
                                {
                                    report.FileDifferences.Add(new FileDifference(relativePath, FileDifferenceType.OnlyInApplication, "File exists only in application.",
                                        applicationFileInfo: appFileInfo, backupFileInfo: null));
                                }
                            }

                            foreach (var (relativePath, backupFileInfo) in destFiles)
                            {
                                if (!sourceFiles.ContainsKey(relativePath))
                                {
                                    report.FileDifferences.Add(new FileDifference(relativePath, FileDifferenceType.OnlyInBackup, "File exists only in backup.",
                                        applicationFileInfo: null, backupFileInfo: backupFileInfo));
                                }
                            }
                        }
                    });
                }
            });
        }

        public async Task BackupAsync(
            IEnumerable<AppConfig> configs,
            string backupRoot,
            SyncMode mode,
            IProgress<int>? progress = null,
            AppStatusUpdateCallback? perAppStatusCallback = null)
        {
            var appList = configs.ToList();
            int totalApps = appList.Count;
            if (totalApps == 0) { progress?.Report(100); return; }
            int processedApps = 0;

            await Task.Run(() =>
            {
                foreach (var app in appList)
                {
                    ProcessSingleApp(app, backupRoot, perAppStatusCallback, (currentApp, report, appDest) =>
                    {
                        bool skipSyncDeletionDueToEmptySource = false;

                        var (sourceFiles, sourcePathIssues, sourceIsEffectivelyEmpty) =
                            GetPathContents(currentApp.Paths, GetSpecialFolderRelativePath, PathIssueSource.Application);
                        report.PathIssues.AddRange(sourcePathIssues);

                        bool criticalSourceIssue = sourcePathIssues.Any(pi =>
                            pi.IssueType == PathIssueType.PathSpecNullOrEmpty ||
                            pi.IssueType == PathIssueType.PathUnexpandable ||
                            pi.IssueType == PathIssueType.PathInaccessible);

                        if (criticalSourceIssue)
                        {
                            _logger.LogWarning("Critical source path problem for app {AppId}. Backup cannot proceed.", currentApp.Id);
                            return;
                        }

                        if (!_fileSystemOperations.DirectoryExists(appDest))
                        {
                            try { _fileSystemOperations.CreateDirectory(appDest); }
                            catch (Exception ex)
                            {
                                report.PathIssues.Add(new PathIssue(appDest, appDest, PathIssueType.PathInaccessible, PathIssueSource.BackupLocation, $"Failed to create backup destination directory: {ex.Message}"));
                                _logger.LogError(ex, "Failed to create backup destination directory for app {AppId}", currentApp.Id);
                                return;
                            }
                        }

                        if (sourceIsEffectivelyEmpty && sourceFiles.Count == 0)
                        {
                            _logger.LogInformation("Source for app {AppId} is effectively empty or all specified source files have issues.", currentApp.Id);
                            if (mode == SyncMode.Sync)
                            {
                                string msg = $"SYNC mode: Source for app {currentApp.Id} is empty. Destination at {appDest} will NOT be cleared.";
                                _logger.LogWarning("SYNC mode: Source for app {CurrentAppId} is empty. Destination at {AppDest} will NOT be cleared.", currentApp.Id, appDest);
                                report.PathIssues.Add(new PathIssue("N/A", appDest, PathIssueType.OperationPrevented, PathIssueSource.BackupLocation, msg));
                                skipSyncDeletionDueToEmptySource = true;
                            }
                            return;
                        }

                        var backedUpRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var (relativePath, appFileInfo) in sourceFiles)
                        {
                            var destBackupFile = Path.Combine(appDest, relativePath);
                            try
                            {
                                var destDir = Path.GetDirectoryName(destBackupFile);
                                if (destDir != null && !_fileSystemOperations.DirectoryExists(destDir))
                                {
                                    _fileSystemOperations.CreateDirectory(destDir);
                                }
                            }
                            catch (Exception ex)
                            {
                                report.PathIssues.Add(new PathIssue(Path.GetDirectoryName(destBackupFile) ?? "N/A", Path.GetDirectoryName(destBackupFile), PathIssueType.PathInaccessible, PathIssueSource.BackupLocation, $"Cannot create directory for '{destBackupFile}': {ex.Message}"));
                                _logger.LogError(ex, "Cannot create directory for {DestFile} in app {AppId}", destBackupFile, currentApp.Id);
                                report.FileDifferences.Add(new FileDifference(relativePath, FileDifferenceType.OperationFailed, $"Failed to create directory for backup file: {ex.Message}", applicationFileInfo: appFileInfo, backupFileInfo: null));
                                continue;
                            }

                            var backupFileInfo = _fileSystemOperations.FileExists(destBackupFile) ? _fileSystemOperations.GetFileInfo(destBackupFile) : null;

                            if (backupFileInfo == null ||
                                appFileInfo.Length != backupFileInfo.Length ||
                                !AreFileTimesClose(appFileInfo.LastWriteTimeUtc, backupFileInfo.LastWriteTimeUtc))
                            {
                                try
                                {
                                    _fileSystemOperations.CopyFile(appFileInfo.FullName, destBackupFile);
                                    _fileSystemOperations.SetLastWriteTimeUtc(destBackupFile, appFileInfo.LastWriteTimeUtc);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Error copying file {SourceFile} to {DestFile} for app {AppId}", appFileInfo.FullName, destBackupFile, currentApp.Id);
                                    report.FileDifferences.Add(new FileDifference(relativePath, FileDifferenceType.OperationFailed, $"Failed to copy/update to backup: {ex.Message}", applicationFileInfo: appFileInfo, backupFileInfo: backupFileInfo));
                                }
                            }
                            if (!report.FileDifferences.Any(fd => fd.RelativePath == relativePath && fd.DifferenceType == FileDifferenceType.OperationFailed))
                            {
                                backedUpRelativePaths.Add(relativePath);
                            }
                        }

                        if (mode == SyncMode.Sync && !skipSyncDeletionDueToEmptySource)
                        {
                            HandleBackupSyncDeletion(report, appDest, sourcePathIssues, backedUpRelativePaths, currentApp.Id);
                        }
                        else if (mode == SyncMode.Sync && skipSyncDeletionDueToEmptySource)
                        {
                            _logger.LogInformation("SYNC Backup: Deletion from backup destination for app {AppId} was skipped because source was empty.", currentApp.Id);
                        }
                    });
                    processedApps++;
                    progress?.Report((int)(processedApps / (double)totalApps * 100));
                }
            });

        }

        private void HandleBackupSyncDeletion(AppSyncReport report, string appDestPath, List<PathIssue> sourcePathIssues, HashSet<string> backedUpRelativePaths, string appId)
        {
            var protectedDeletionRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var protectedDeletionRelativePrefixes = new List<string>();

            foreach (var issue in sourcePathIssues.Where(i => i.Source == PathIssueSource.Application && i.ExpandedPath != null && !string.IsNullOrWhiteSpace(i.PathSpec)))
            {
                bool originalSpecWasDirectory = issue.PathSpec.EndsWith(Path.DirectorySeparatorChar.ToString()) ||
                                               issue.PathSpec.EndsWith(Path.AltDirectorySeparatorChar.ToString());
                string relativePathFromIssue = GetSpecialFolderRelativePath(issue.ExpandedPath!);

                if (!originalSpecWasDirectory && issue.IssueType == PathIssueType.PathNotFound)
                {
                    protectedDeletionRelativePaths.Add(relativePathFromIssue);
                }
                else if (originalSpecWasDirectory && (issue.IssueType == PathIssueType.PathNotFound || issue.IssueType == PathIssueType.PathIsEffectivelyEmpty))
                {
                    var prefix = relativePathFromIssue.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    protectedDeletionRelativePrefixes.Add(prefix);
                }
            }

            var (destFilesForSync, destPathIssuesForSync, _) = GetPathContents([report.AppBackupRootPath], filePath => Path.GetRelativePath(appDestPath, filePath), PathIssueSource.BackupLocation, appDestPath);
            report.PathIssues.AddRange(destPathIssuesForSync);

            bool criticalDestPathIssueForSync = destPathIssuesForSync.Any(pi =>
                pi.IssueType == PathIssueType.PathSpecNullOrEmpty ||
                pi.IssueType == PathIssueType.PathUnexpandable ||
                pi.IssueType == PathIssueType.PathInaccessible);

            if (criticalDestPathIssueForSync)
            {
                _logger.LogWarning("SYNC Backup: Critical issue reading backup destination paths for app {AppId}. Cannot perform deletions from backup.", appId);
                report.PathIssues.Add(new PathIssue(appDestPath, appDestPath, PathIssueType.OperationPrevented, PathIssueSource.BackupLocation, "SYNC Backup: Deletion step skipped due to critical issues accessing backup destination paths."));
            }
            else
            {
                foreach (var (relativeDestPath, currentBackupFileInfo) in destFilesForSync)
                {
                    if (!backedUpRelativePaths.Contains(relativeDestPath))
                    {
                        bool toBePreserved = protectedDeletionRelativePaths.Contains(relativeDestPath) ||
                                             protectedDeletionRelativePrefixes.Any(p => relativeDestPath.StartsWith(p, StringComparison.OrdinalIgnoreCase));

                        if (toBePreserved)
                        {
                            string originalPathSpec = sourcePathIssues.FirstOrDefault(pi => pi.ExpandedPath != null && GetSpecialFolderRelativePath(pi.ExpandedPath!) == (protectedDeletionRelativePaths.Contains(relativeDestPath) ? relativeDestPath : protectedDeletionRelativePrefixes.First(p => relativeDestPath.StartsWith(p, StringComparison.OrdinalIgnoreCase)).TrimEnd(Path.DirectorySeparatorChar)))?.PathSpec ?? "N/A";
                            _logger.LogInformation("SYNC Backup: Preserving backup item {DestFileFullPath} (corresponds to source PathSpec '{OriginalPathSpec}') as its source was missing or empty.", currentBackupFileInfo.FullName, originalPathSpec);
                            report.FileDifferences.Add(new FileDifference(
                                relativeDestPath,
                                FileDifferenceType.OnlyInBackup,
                                $"Preserved in backup (SYNC mode). Corresponding application item (PathSpec: {originalPathSpec}) was missing, or source directory was empty/missing.",
                                applicationFileInfo: null, backupFileInfo: currentBackupFileInfo
                            ));
                            continue;
                        }

                        try
                        {
                            _logger.LogInformation("SYNC Backup: Deleting {DestFileFullPath} from backup as it's not in the application source for app {AppId}", currentBackupFileInfo.FullName, appId);
                            _fileSystemOperations.DeleteFile(currentBackupFileInfo.FullName);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error deleting file {DestFileFullPath} from backup during SYNC backup for app {AppId}", currentBackupFileInfo.FullName, appId);
                            report.FileDifferences.Add(new FileDifference(relativeDestPath, FileDifferenceType.OperationFailed, $"Failed to delete from backup (SYNC mode): {ex.Message}", applicationFileInfo: null, backupFileInfo: currentBackupFileInfo));
                        }
                    }
                }
                _fileSystemOperations.DeleteEmptyDirectories(appDestPath);
            }
        }

        public async Task RestoreAsync(
            IEnumerable<AppConfig> configs,
            string backupRoot,
            SyncMode mode,
            IProgress<int>? progress = null,
            AppStatusUpdateCallback? perAppStatusCallback = null)
        {
            var appList = configs.ToList();
            int totalApps = appList.Count;
            if (totalApps == 0) { progress?.Report(100); return; }
            int processedApps = 0;
            await Task.Run(() =>
            {
                foreach (var app in appList)
                {
                    ProcessSingleApp(app, backupRoot, perAppStatusCallback, (currentApp, report, appBackupSourcePath) =>
                    {
                        bool skipSyncDeletionDueToEmptySource = false;

                        var (backupFiles, backupPathIssues, backupIsEffectivelyEmpty) =
                            GetPathContents([report.AppBackupRootPath],
                                            filePath => Path.GetRelativePath(appBackupSourcePath, filePath),
                                            PathIssueSource.BackupLocation,
                                            appBackupSourcePath);
                        report.PathIssues.AddRange(backupPathIssues);

                        bool criticalBackupSourceIssue = backupPathIssues.Any(pi =>
                            pi.IssueType == PathIssueType.PathSpecNullOrEmpty ||
                            pi.IssueType == PathIssueType.PathUnexpandable ||
                            (pi.IssueType == PathIssueType.PathNotFound && pi.PathSpec == report.AppBackupRootPath) ||
                            pi.IssueType == PathIssueType.PathInaccessible);

                        if (criticalBackupSourceIssue && backupFiles.Count == 0)
                        {
                            _logger.LogWarning("Critical problem with backup source for app {AppId} at {BackupPath} and no files found. Restore cannot proceed.", currentApp.Id, appBackupSourcePath);
                            return;
                        }

                        if (backupIsEffectivelyEmpty && backupFiles.Count == 0)
                        {
                            _logger.LogInformation("Backup source for app {AppId} is effectively empty. Nothing to restore.", currentApp.Id);
                            if (mode == SyncMode.Sync)
                            {
                                string appPathsSummary = currentApp.Paths.Count > 0 ? string.Join(", ", currentApp.Paths.Take(2)) + (currentApp.Paths.Count > 2 ? "..." : "") : "N/A";
                                string msg = $"SYNC mode: Backup source for app {currentApp.Id} is empty. Application files at '{appPathsSummary}' will NOT be cleared.";
                                _logger.LogWarning("SYNC mode: Backup source for app {CurrentAppId} is empty. Application files at '{AppPathsSummary}' will NOT be cleared.", currentApp.Id, appPathsSummary);
                                report.PathIssues.Add(new PathIssue(appPathsSummary, null, PathIssueType.OperationPrevented, PathIssueSource.Application, msg));
                                skipSyncDeletionDueToEmptySource = true;
                            }
                            return;
                        }

                        var restoredRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (backupFiles.Count != 0)
                        {
                            foreach (var (relativePath, fileInBackupInfo) in backupFiles)
                            {
                                string? appFileDestPath = null;
                                try
                                {
                                    appFileDestPath = SpecialFolderUtil.ExpandSpecialFolders(relativePath);
                                    if (string.IsNullOrWhiteSpace(appFileDestPath) || (!Path.IsPathRooted(appFileDestPath) && !appFileDestPath.Contains('%')))
                                    {
                                        string errMsg = $"Could not reliably expand restore destination path for backup item '{relativePath}'.";
                                        _logger.LogWarning("Could not reliably expand restore destination path for backup item '{RelativePath}'. AppId: {AppId}", relativePath, currentApp.Id);
                                        report.PathIssues.Add(new PathIssue(relativePath, null, PathIssueType.PathUnexpandable, PathIssueSource.Application, errMsg));
                                        report.FileDifferences.Add(new FileDifference(relativePath, FileDifferenceType.OperationFailed, errMsg, applicationFileInfo: null, backupFileInfo: fileInBackupInfo));
                                        continue;
                                    }

                                    var destDir = Path.GetDirectoryName(appFileDestPath);
                                    if (destDir != null && !_fileSystemOperations.DirectoryExists(destDir))
                                    {
                                        _fileSystemOperations.CreateDirectory(destDir);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    string errMsg = $"Failed to prepare application destination directory for '{appFileDestPath ?? relativePath}': {ex.Message}";
                                    _logger.LogError(ex, "Error preparing destination for app {AppId}, backup item '{RelativePath}'", currentApp.Id, relativePath);
                                    report.PathIssues.Add(new PathIssue(relativePath, appFileDestPath, PathIssueType.PathInaccessible, PathIssueSource.Application, errMsg));
                                    report.FileDifferences.Add(new FileDifference(relativePath, FileDifferenceType.OperationFailed, $"Failed to create/access application destination directory for backup item '{relativePath}': {ex.Message}", applicationFileInfo: null, backupFileInfo: fileInBackupInfo));
                                    continue;
                                }

                                var currentAppFileInfo = _fileSystemOperations.FileExists(appFileDestPath) ? _fileSystemOperations.GetFileInfo(appFileDestPath) : null;

                                if (currentAppFileInfo == null ||
                                    fileInBackupInfo.Length != currentAppFileInfo.Length ||
                                    !AreFileTimesClose(fileInBackupInfo.LastWriteTimeUtc, currentAppFileInfo.LastWriteTimeUtc))
                                {
                                    try
                                    {
                                        _fileSystemOperations.CopyFile(fileInBackupInfo.FullName, appFileDestPath);
                                        _fileSystemOperations.SetLastWriteTimeUtc(appFileDestPath, fileInBackupInfo.LastWriteTimeUtc);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Error restoring file {BackupFile} to {RestoreDestFile} for app {AppId} (Backup Item: {RelativePath})", fileInBackupInfo.FullName, appFileDestPath, currentApp.Id, relativePath);
                                        report.FileDifferences.Add(new FileDifference(relativePath, FileDifferenceType.OperationFailed, $"Failed to copy/update to application: {ex.Message}", applicationFileInfo: currentAppFileInfo, backupFileInfo: fileInBackupInfo));
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
                            HandleRestoreSyncDeletion(report, currentApp, backupFiles, restoredRelativePaths);
                        }
                        else if (mode == SyncMode.Sync && skipSyncDeletionDueToEmptySource)
                        {
                            _logger.LogInformation("SYNC Restore: Deletion from app location for app {AppId} was skipped because backup source was empty.", currentApp.Id);
                        }
                    });
                    processedApps++;
                    progress?.Report((int)(processedApps / (double)totalApps * 100));
                }
            });

        }

        private void HandleRestoreSyncDeletion(AppSyncReport report, AppConfig app, Dictionary<string, FileInfo> backupFiles, HashSet<string> restoredRelativePaths)
        {
            var preserveLiveRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var preserveLiveRelativePrefixes = new List<string>();

            foreach (var pathSpec in app.Paths)
            {
                if (string.IsNullOrWhiteSpace(pathSpec)) continue;
                var expandedPathSpec = SpecialFolderUtil.ExpandSpecialFolders(pathSpec);
                if (string.IsNullOrWhiteSpace(expandedPathSpec)) continue;

                string expectedRelPathKey = GetSpecialFolderRelativePath(expandedPathSpec);
                bool originalSpecWasDirectory = pathSpec.EndsWith(Path.DirectorySeparatorChar.ToString()) ||
                                               pathSpec.EndsWith(Path.AltDirectorySeparatorChar.ToString());

                if (!originalSpecWasDirectory)
                {
                    if (!backupFiles.ContainsKey(expectedRelPathKey))
                    {
                        preserveLiveRelativePaths.Add(expectedRelPathKey);
                    }
                }
                else
                {
                    var expectedRelPathPrefix = expectedRelPathKey.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    bool backupHasFilesForThisDirSpec = backupFiles.Keys.Any(k => k.StartsWith(expectedRelPathPrefix, StringComparison.OrdinalIgnoreCase));
                    if (!backupHasFilesForThisDirSpec)
                    {
                        preserveLiveRelativePrefixes.Add(expectedRelPathPrefix);
                    }
                }
            }

            var (currentAppFiles, currentAppPathIssues, _) =
                GetPathContents(app.Paths, GetSpecialFolderRelativePath, PathIssueSource.Application);
            report.PathIssues.AddRange(currentAppPathIssues);

            bool criticalAppPathIssueForSync = currentAppPathIssues.Any(pi =>
                pi.IssueType == PathIssueType.PathSpecNullOrEmpty ||
                pi.IssueType == PathIssueType.PathUnexpandable ||
                pi.IssueType == PathIssueType.PathInaccessible);

            if (criticalAppPathIssueForSync)
            {
                _logger.LogWarning("SYNC Restore: Critical issue reading original application paths for app {AppId}. Cannot perform deletions.", app.Id);
                report.PathIssues.Add(new PathIssue(string.Join(";", app.Paths.Take(3)) + (app.Paths.Count > 3 ? "..." : ""), null, PathIssueType.OperationPrevented, PathIssueSource.Application, "SYNC Restore: Deletion step skipped due to critical issues accessing original application paths."));
            }
            else
            {
                foreach (var (liveRelativePath, liveAppFileInfo) in currentAppFiles)
                {
                    if (!restoredRelativePaths.Contains(liveRelativePath))
                    {
                        bool toBePreserved = preserveLiveRelativePaths.Contains(liveRelativePath) ||
                                             preserveLiveRelativePrefixes.Any(p => liveRelativePath.StartsWith(p, StringComparison.OrdinalIgnoreCase));

                        string originalPathSpecCorrelated = app.Paths.FirstOrDefault(ps =>
                        {
                            if (string.IsNullOrWhiteSpace(ps)) return false;
                            var expPs = SpecialFolderUtil.ExpandSpecialFolders(ps);
                            if (string.IsNullOrWhiteSpace(expPs)) return false;
                            var relPs = GetSpecialFolderRelativePath(expPs);
                            if (preserveLiveRelativePaths.Contains(liveRelativePath)) return relPs == liveRelativePath;
                            if (preserveLiveRelativePrefixes.Any(pfx => liveRelativePath.StartsWith(pfx, StringComparison.OrdinalIgnoreCase)))
                            {
                                var matchingPrefix = preserveLiveRelativePrefixes.First(pfx => liveRelativePath.StartsWith(pfx, StringComparison.OrdinalIgnoreCase));
                                return (relPs.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar) == matchingPrefix;
                            }
                            return false;
                        }) ?? liveRelativePath;

                        if (toBePreserved)
                        {
                            _logger.LogInformation("SYNC Restore: Preserving application file {AppFileFullPath} (tokenized path '{LiveRelativePath}', related to AppConfig.Path '{OriginalPathSpec}') as its item was missing/empty in backup.", liveAppFileInfo.FullName, liveRelativePath, originalPathSpecCorrelated);
                            report.FileDifferences.Add(new FileDifference(
                                liveRelativePath,
                                FileDifferenceType.OnlyInApplication,
                                $"Preserved in application (SYNC mode). Corresponding backup item for '{originalPathSpecCorrelated}' was missing or backup directory empty.",
                                applicationFileInfo: liveAppFileInfo, backupFileInfo: null
                            ));
                            continue;
                        }

                        bool restoreAttemptedAndFailed = report.FileDifferences.Any(fd =>
                            fd.RelativePath == liveRelativePath &&
                            fd.DifferenceType == FileDifferenceType.OperationFailed &&
                            fd.BackupFileInfo != null);

                        if (!restoreAttemptedAndFailed)
                        {
                            try
                            {
                                _logger.LogInformation("SYNC Restore: Deleting {AppFileFullPath} from application (tokenized path '{LiveRelativePath}') as it's not in the backup and not preserved for app {AppId}", liveAppFileInfo.FullName, liveRelativePath, app.Id);
                                _fileSystemOperations.DeleteFile(liveAppFileInfo.FullName);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error deleting file {AppFileFullPath} from application (tokenized path '{LiveRelativePath}') during SYNC restore for app {AppId}", liveAppFileInfo.FullName, liveRelativePath, app.Id);
                                report.FileDifferences.Add(new FileDifference(liveRelativePath, FileDifferenceType.OperationFailed, $"Failed to delete from application (SYNC mode): {ex.Message}", applicationFileInfo: liveAppFileInfo, backupFileInfo: null));
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

                    string? pathToClean = isLikelyDirectorySpec ? expandedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : Path.GetDirectoryName(expandedPath);

                    if (isLikelyDirectorySpec)
                    {
                        _fileSystemOperations.DeleteEmptyDirectories(pathToClean);
                    }
                }
            }
        }

        private static string GetDriveLetterRelativePath(string fullPath)
        {
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root) || root.Length < 2 || !Path.IsPathRooted(fullPath) || root.Length > 3)
            {
                return fullPath;
            }
            var driveLetter = root[0].ToString();
            var rest = fullPath[root.Length..];
            return Path.Combine(driveLetter, rest);
        }

        private static string GetSpecialFolderRelativePath(string fullPath)
        {
            var specialPath = SpecialFolderUtil.ConvertToSpecialFolderPath(fullPath);
            if (specialPath == fullPath && Path.IsPathRooted(fullPath))
            {
                if (fullPath.StartsWith(@"\\") || fullPath.StartsWith(@"//"))
                {
                    var parts = fullPath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        return Path.Combine(parts);
                    }
                    return fullPath;
                }
                return GetDriveLetterRelativePath(fullPath);
            }
            return specialPath;
        }
    }
}