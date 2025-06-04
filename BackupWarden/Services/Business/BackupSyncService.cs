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
using System.Threading.Tasks;

namespace BackupWarden.Services.Business
{
    public interface IBackupSyncService
    {
        Task UpdateSyncStatusAsync(IEnumerable<AppConfig> apps, string destinationRoot, Action<AppConfig, SyncStatus>? perAppStatusCallback = null);
        Task RestoreAsync(IEnumerable<AppConfig> configs, string destinationRoot, SyncMode mode, IProgress<int>? progress = null, Action<AppConfig, SyncStatus>? perAppStatusCallback = null);
        Task BackupAsync(IEnumerable<AppConfig> configs, string destinationRoot, SyncMode mode, IProgress<int>? progress = null, Action<AppConfig, SyncStatus>? perAppStatusCallback = null);
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

        // Helper to allow for small differences in file write times, e.g., due to FAT32 precision
        private static bool AreFileTimesClose(DateTime t1, DateTime t2, TimeSpan? tolerance = null)
        {
            tolerance ??= TimeSpan.FromSeconds(2); // Default 2-second tolerance
            return (t1 > t2) ? (t1 - t2 <= tolerance) : (t2 - t1 <= tolerance);
        }

        private static (Dictionary<string, FileInfo> FilesByRelativePath, bool HasProblem, bool IsEffectivelyEmpty) GetPathContents(
            IEnumerable<string> pathSpecs,
            Func<string, string> getRelativePathFunc,
            string? baseDirectoryForRelativePath = null) // Used when getRelativePathFunc needs a base (e.g., Path.GetRelativePath)
        {
            var fileDetails = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            bool hasProblem = false;
            bool isEffectivelyEmpty = true;

            if (pathSpecs == null || !pathSpecs.Any())
            {
                return (fileDetails, true, true); // Problem: no paths configured
            }

            foreach (var pathSpec in pathSpecs)
            {
                if (string.IsNullOrWhiteSpace(pathSpec))
                {
                    hasProblem = true; // Problem: null or whitespace path spec
                    continue;
                }
                var expandedPath = SpecialFolderUtil.ExpandSpecialFolders(pathSpec);
                if (string.IsNullOrWhiteSpace(expandedPath))
                {
                    hasProblem = true; // Problem: unexpandable path
                    continue;
                }

                bool isPathDirectorySpec = expandedPath.EndsWith(Path.DirectorySeparatorChar.ToString()) ||
                                       expandedPath.EndsWith(Path.AltDirectorySeparatorChar.ToString());
                var actualPath = isPathDirectorySpec ? expandedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : expandedPath;

                if (isPathDirectorySpec)
                {
                    if (!Directory.Exists(actualPath)) { hasProblem = true; continue; }
                    try
                    {
                        var filesInDir = Directory.EnumerateFiles(actualPath, "*", SearchOption.AllDirectories).ToList();
                        if (filesInDir.Count != 0) isEffectivelyEmpty = false;
                        foreach (var file in filesInDir)
                        {
                            var fi = new FileInfo(file);
                            var relPath = baseDirectoryForRelativePath != null ? Path.GetRelativePath(baseDirectoryForRelativePath, file) : getRelativePathFunc(file);
                            if (!fileDetails.ContainsKey(relPath)) fileDetails[relPath] = fi;
                        }
                    }
                    catch (Exception ex) when (ex is DirectoryNotFoundException || ex is UnauthorizedAccessException)
                    {
                        // Log this specific issue if needed, e.g., _logger.LogWarning("Could not access directory {ActualPath}", actualPath);
                        hasProblem = true;
                    }
                }
                else // File
                {
                    if (!File.Exists(actualPath)) { hasProblem = true; continue; }
                    isEffectivelyEmpty = false;
                    try
                    {
                        var fi = new FileInfo(actualPath);
                        var relPath = baseDirectoryForRelativePath != null ? Path.GetRelativePath(baseDirectoryForRelativePath, actualPath) : getRelativePathFunc(actualPath);
                        if (!fileDetails.ContainsKey(relPath)) fileDetails[relPath] = fi;
                    }
                    catch (Exception ex) when (ex is FileNotFoundException || ex is UnauthorizedAccessException)
                    {
                        // Log this specific issue if needed
                        hasProblem = true;
                    }
                }
            }
            // If any path had a problem, the overall result is problematic, and we consider it effectively empty for safety.
            return (fileDetails, hasProblem, hasProblem || isEffectivelyEmpty);
        }


        public async Task UpdateSyncStatusAsync(
            IEnumerable<AppConfig> apps,
            string destinationRoot,
            Action<AppConfig, SyncStatus>? perAppStatusCallback = null)
        {
            await Task.Run(() =>
            {
                foreach (AppConfig app in apps)
                {
                    if (app == null) continue;
                    try
                    {
                        var (sourceFiles, sourceHasProblem, sourceIsEffectivelyEmpty) =
                            GetPathContents(app.Paths, GetSpecialFolderRelativePath);

                        if (sourceHasProblem)
                        {
                            perAppStatusCallback?.Invoke(app, SyncStatus.SourcePathProblem);
                            continue;
                        }

                        var appDestPath = Path.Combine(destinationRoot, app.Id);

                        if (sourceIsEffectivelyEmpty)
                        {
                            if (Directory.Exists(appDestPath) && Directory.EnumerateFileSystemEntries(appDestPath).Any())
                            {
                                perAppStatusCallback?.Invoke(app, SyncStatus.OutOfSync); // Source empty, backup has files
                            }
                            else
                            {
                                // Source empty, backup also empty/non-existent.
                                // This is a specific state; SourcePathProblem is appropriate as it signals an unusual source state.
                                perAppStatusCallback?.Invoke(app, SyncStatus.SourcePathProblem);
                            }
                            continue;
                        }

                        // Source is valid and has content.
                        if (!Directory.Exists(appDestPath))
                        {
                            perAppStatusCallback?.Invoke(app, SyncStatus.OutOfSync); // Source has content, backup doesn't exist.
                            continue;
                        }

                        var (destFiles, destHasProblem, destIsEffectivelyEmpty) =
                            GetPathContents([appDestPath + Path.DirectorySeparatorChar],
                                            filePath => Path.GetRelativePath(appDestPath, filePath), // Relative to appDestPath
                                            appDestPath);


                        if (destHasProblem)
                        {
                            perAppStatusCallback?.Invoke(app, SyncStatus.BackupPathProblem); // Problem reading backup contents.
                            continue;
                        }

                        if (destIsEffectivelyEmpty && !sourceIsEffectivelyEmpty)
                        {
                            perAppStatusCallback?.Invoke(app, SyncStatus.OutOfSync); // Source has files, backup is empty.
                            continue;
                        }

                        // Both source and destination have content (or both are validly empty, handled by sourceIsEffectivelyEmpty). Compare them.
                        bool inSync = true;
                        if (sourceFiles.Count != destFiles.Count)
                        {
                            inSync = false;
                        }
                        else
                        {
                            foreach (var (relativePath, srcFileInfo) in sourceFiles)
                            {
                                if (!destFiles.TryGetValue(relativePath, out var dstFileInfo) ||
                                    srcFileInfo.Length != dstFileInfo.Length ||
                                    !AreFileTimesClose(srcFileInfo.LastWriteTimeUtc, dstFileInfo.LastWriteTimeUtc))
                                {
                                    inSync = false;
                                    break;
                                }
                            }
                        }

                        // Final check: ensure destination doesn't have extra files not in source
                        if (inSync)
                        {
                            foreach (var relativePath in destFiles.Keys)
                            {
                                if (!sourceFiles.ContainsKey(relativePath))
                                {
                                    inSync = false;
                                    break;
                                }
                            }
                        }

                        perAppStatusCallback?.Invoke(app, inSync ? SyncStatus.InSync : SyncStatus.OutOfSync);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error checking sync status for app {AppId}", app?.Id);
                        if (app != null) perAppStatusCallback?.Invoke(app, SyncStatus.Failed);
                    }
                }
            });
        }


        public async Task BackupAsync(
            IEnumerable<AppConfig> configs,
            string destinationRoot,
            SyncMode mode,
            IProgress<int>? progress = null,
            Action<AppConfig, SyncStatus>? perAppStatusCallback = null)
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

                    perAppStatusCallback?.Invoke(app, SyncStatus.Syncing);
                    var appDest = Path.Combine(destinationRoot, app.Id);
                    Directory.CreateDirectory(appDest);

                    var (sourceFiles, sourceHasProblem, sourceIsEffectivelyEmpty) =
                        GetPathContents(app.Paths, GetSpecialFolderRelativePath);

                    if (sourceHasProblem)
                    {
                        _logger.LogWarning("Source path problem for app {AppId}. Skipping backup.", app.Id);
                        perAppStatusCallback?.Invoke(app, SyncStatus.SourcePathProblem);
                        processedApps++;
                        progress?.Report((int)(processedApps / (double)totalApps * 100));
                        continue;
                    }

                    if (sourceIsEffectivelyEmpty)
                    {
                        _logger.LogInformation("Source for app {AppId} is effectively empty.", app.Id);
                        if (mode == SyncMode.Sync)
                        {
                            _logger.LogWarning("SYNC mode: Source for app {AppId} is empty. Destination at {AppDest} will NOT be cleared to prevent data loss.", app.Id, appDest);
                        }
                        // Report SourcePathProblem as the outcome status for this operation if source is empty.
                        perAppStatusCallback?.Invoke(app, SyncStatus.SourcePathProblem);
                        processedApps++;
                        progress?.Report((int)(processedApps / (double)totalApps * 100));
                        continue;
                    }

                    try
                    {
                        var backedUpRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var (relativePath, srcFileInfo) in sourceFiles)
                        {
                            var destFile = Path.Combine(appDest, relativePath);
                            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

                            var destFileInfo = File.Exists(destFile) ? new FileInfo(destFile) : null;

                            if (destFileInfo == null ||
                                srcFileInfo.Length != destFileInfo.Length ||
                                !AreFileTimesClose(srcFileInfo.LastWriteTimeUtc, destFileInfo.LastWriteTimeUtc))
                            {
                                await CopyFileAsync(srcFileInfo.FullName, destFile);
                                File.SetLastWriteTimeUtc(destFile, srcFileInfo.LastWriteTimeUtc);
                            }
                            backedUpRelativePaths.Add(relativePath);
                        }

                        if (mode == SyncMode.Sync)
                        {
                            var filesInDest = Directory.Exists(appDest)
                                ? Directory.EnumerateFiles(appDest, "*", SearchOption.AllDirectories)
                                : [];

                            foreach (var destFileFullPath in filesInDest)
                            {
                                var relativeDestPath = Path.GetRelativePath(appDest, destFileFullPath);
                                if (!backedUpRelativePaths.Contains(relativeDestPath))
                                {
                                    _logger.LogInformation("SYNC Backup: Deleting {DestFileFullPath} as it's not in the source for app {AppId}", destFileFullPath, app.Id);
                                    await _retryPolicy.ExecuteAsync(() => Task.Run(() => File.Delete(destFileFullPath)));
                                }
                            }
                            DeleteEmptyDirectories(appDest);
                        }
                        perAppStatusCallback?.Invoke(app, SyncStatus.InSync);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error backing up app {AppId}", app.Id);
                        perAppStatusCallback?.Invoke(app, SyncStatus.Failed);
                    }
                    processedApps++;
                    progress?.Report((int)(processedApps / (double)totalApps * 100));
                }
            });
        }

        private readonly struct ExpandedPathInfo(string original, string expanded, bool isDir)
        {
            public string Original { get; } = original;
            public string Expanded { get; } = expanded;
            public bool IsDir { get; } = isDir;
        }

        public async Task RestoreAsync(
            IEnumerable<AppConfig> configs,
            string destinationRoot, // This is the root of backups, e.g., D:\Backups
            SyncMode mode,
            IProgress<int>? progress = null,
            Action<AppConfig, SyncStatus>? perAppStatusCallback = null)
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

                    perAppStatusCallback?.Invoke(app, SyncStatus.Syncing);
                    var appBackupSourceRoot = Path.Combine(destinationRoot, app.Id);

                    if (app.Paths == null || app.Paths.Count == 0)
                    {
                        _logger.LogWarning("No target paths configured for restore for app {AppId}. Skipping.", app.Id);
                        perAppStatusCallback?.Invoke(app, SyncStatus.SourcePathProblem); // SourcePathProblem refers to AppConfig.Paths being unconfigured
                        processedApps++;
                        progress?.Report((int)(processedApps / (double)totalApps * 100));
                        continue;
                    }

                    var expandedAppTargetBasePaths = app.Paths
                        .Select(p =>
                        {
                            var expanded = SpecialFolderUtil.ExpandSpecialFolders(p);
                            if (string.IsNullOrWhiteSpace(expanded)) return (ExpandedPathInfo?)null;
                            bool isDir = expanded.EndsWith(Path.DirectorySeparatorChar.ToString()) || expanded.EndsWith(Path.AltDirectorySeparatorChar.ToString());
                            return new ExpandedPathInfo(p, Path.GetFullPath(isDir ? expanded.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : expanded), isDir);
                        })
                        .Where(p => p.HasValue && !string.IsNullOrWhiteSpace(p.Value.Expanded))
                        .Select(p => p!.Value)
                        .ToList();

                    if (expandedAppTargetBasePaths.Count == 0)
                    {
                        _logger.LogWarning("All target paths for app {AppId} are invalid or unexpandable. Skipping restore.", app.Id);
                        perAppStatusCallback?.Invoke(app, SyncStatus.SourcePathProblem); // SourcePathProblem refers to AppConfig.Paths being invalid
                        processedApps++;
                        progress?.Report((int)(processedApps / (double)totalApps * 100));
                        continue;
                    }

                    // Use GetPathContents to read files from the backup source directory
                    var (backupFiles, backupHasProblem, backupIsEffectivelyEmpty) =
                        GetPathContents(
                            [appBackupSourceRoot + Path.DirectorySeparatorChar], // Treat the app's backup root as a single directory path spec
                            filePath => Path.GetRelativePath(appBackupSourceRoot, filePath), // Relative paths are from the app's backup root
                            appBackupSourceRoot // Base directory for Path.GetRelativePath
                        );

                    if (backupHasProblem) // This covers Directory.Exists issues and access issues within GetPathContents
                    {
                        _logger.LogWarning("Problem reading backup source for app {AppId} at {AppBackupSourceRoot}. It might be inaccessible.", app.Id, appBackupSourceRoot);
                        perAppStatusCallback?.Invoke(app, SyncStatus.BackupPathProblem);
                        processedApps++;
                        progress?.Report((int)(processedApps / (double)totalApps * 100));
                        continue;
                    }

                    if (backupIsEffectivelyEmpty)
                    {
                        _logger.LogInformation("Backup source for app {AppId} at {AppBackupSourceRoot} is effectively empty.", app.Id, appBackupSourceRoot);
                        if (mode == SyncMode.Sync)
                        {
                            _logger.LogWarning("SYNC mode: Backup source for app {AppId} is empty. Live application paths will NOT be cleared.", app.Id);
                        }
                        perAppStatusCallback?.Invoke(app, SyncStatus.BackupPathProblem); // Report BackupPathProblem as the outcome
                        processedApps++;
                        progress?.Report((int)(processedApps / (double)totalApps * 100));
                        continue;
                    }

                    try
                    {
                        var restoredLiveFileFullPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        // Iterate over the files obtained from GetPathContents
                        foreach (var (relativePathFromBackupAppRoot, backupFileInfo) in backupFiles)
                        {
                            var pathAfterUserMapping = SpecialFolderUtil.MapBackupUserPathToCurrentUser(relativePathFromBackupAppRoot);
                            var finalLivePathCandidate = SpecialFolderUtil.ExpandSpecialFolders(pathAfterUserMapping);

                            if (string.IsNullOrWhiteSpace(finalLivePathCandidate))
                            {
                                _logger.LogDebug("Skipping restore of backup file {RelativePath} as its target path could not be determined for app {AppId}", relativePathFromBackupAppRoot, app.Id);
                                continue;
                            }
                            var finalLiveFullPath = Path.GetFullPath(finalLivePathCandidate);

                            bool isInConfiguredPath = expandedAppTargetBasePaths.Any(etp =>
                                etp.IsDir
                                    ? finalLiveFullPath.StartsWith(etp.Expanded + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || finalLiveFullPath.Equals(etp.Expanded, StringComparison.OrdinalIgnoreCase)
                                    : finalLiveFullPath.Equals(etp.Expanded, StringComparison.OrdinalIgnoreCase)
                            );

                            if (!isInConfiguredPath)
                            {
                                _logger.LogDebug("Skipping restore of backup file {RelativePath} to {FinalLiveFullPath} as it's not under a configured target path for app {AppId}", relativePathFromBackupAppRoot, finalLiveFullPath, app.Id);
                                continue;
                            }

                            Directory.CreateDirectory(Path.GetDirectoryName(finalLiveFullPath)!);
                            // Use backupFileInfo.FullName which is the absolute path to the file in the backup
                            await RestoreFileInternalAsync(backupFileInfo.FullName, finalLiveFullPath);
                            restoredLiveFileFullPaths.Add(finalLiveFullPath);
                        }

                        if (mode == SyncMode.Sync)
                        {
                            foreach (var targetPathInfo in expandedAppTargetBasePaths)
                            {
                                string currentLivePathTarget = targetPathInfo.Expanded;
                                if (targetPathInfo.IsDir)
                                {
                                    if (Directory.Exists(currentLivePathTarget))
                                    {
                                        // Get all files currently in the live target directory
                                        var (liveFilesToCheck, _, _) = GetPathContents(
                                            [currentLivePathTarget + Path.DirectorySeparatorChar],
                                            filePath => Path.GetFullPath(filePath), // We need full paths for comparison with restoredLiveFileFullPaths
                                            currentLivePathTarget
                                        );

                                        foreach (var liveFileFullPath in liveFilesToCheck.Keys) // liveFilesToCheck.Keys are full paths here
                                        {
                                            if (!restoredLiveFileFullPaths.Contains(liveFileFullPath))
                                            {
                                                _logger.LogInformation("SYNC Restore: Deleting {LiveFileFullPath} from app {AppId} as it's not in the current backup set being restored.", liveFileFullPath, app.Id);
                                                await _retryPolicy.ExecuteAsync(() => Task.Run(() => File.Delete(liveFileFullPath)));
                                            }
                                        }
                                        DeleteEmptyDirectories(currentLivePathTarget);
                                    }
                                }
                                else // Target path is a file
                                {
                                    if (File.Exists(currentLivePathTarget) && !restoredLiveFileFullPaths.Contains(Path.GetFullPath(currentLivePathTarget)))
                                    {
                                        _logger.LogInformation("SYNC Restore: Deleting file {CurrentLivePathTarget} from app {AppId} as it's not in the current backup set being restored.", currentLivePathTarget, app.Id);
                                        await _retryPolicy.ExecuteAsync(() => Task.Run(() => File.Delete(currentLivePathTarget)));
                                    }
                                }
                            }
                        }
                        perAppStatusCallback?.Invoke(app, SyncStatus.InSync);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error restoring app {AppId}", app.Id);
                        perAppStatusCallback?.Invoke(app, SyncStatus.Failed);
                    }
                    processedApps++;
                    progress?.Report((int)(processedApps / (double)totalApps * 100));
                }
            });
        }

        private static async Task RestoreFileInternalAsync(string backupFile, string destFile)
        {
            // Ensure destination directory exists (already done in RestoreAsync, but good for standalone use)
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

            if (File.Exists(backupFile)) // Should always exist if called from RestoreAsync loop
            {
                await CopyFileAsync(backupFile, destFile);
                var backupInfo = new FileInfo(backupFile);
                try
                {
                    File.SetLastWriteTimeUtc(destFile, backupInfo.LastWriteTimeUtc);
                }
                catch (IOException ex)
                {
                    // Log this? Sometimes setting timestamp can fail on certain file systems or if file is in use.
                    Debug.WriteLine($"Could not set LastWriteTimeUtc for {destFile}: {ex.Message}");
                }
            }
        }

        private static string GetDriveLetterRelativePath(string fullPath)
        {
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root) || root.Length < 2 || !Path.IsPathRooted(fullPath) || root.Length > 3) // C:\ or \\server\share
                return fullPath; // Not a standard drive letter path or already relative

            var driveLetter = root[0].ToString();
            var rest = fullPath[root.Length..];
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
            const int bufferSize = 81920; // 80 KB
            await _retryPolicy.ExecuteAsync(async () =>
            {
                using var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
                using var destStream = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
                await sourceStream.CopyToAsync(destStream);
            });
        }

        private static void DeleteEmptyDirectories(string rootDirectory)
        {
            if (!Directory.Exists(rootDirectory)) return;
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(rootDirectory, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length)) // Process deeper directories first
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        try
                        {
                            Directory.Delete(dir);
                        }
                        catch (IOException ex)
                        {
                            Debug.WriteLine($"Could not delete empty directory {dir}: {ex.Message}");
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            Debug.WriteLine($"Access denied deleting empty directory {dir}: {ex.Message}");
                        }
                    }
                }
                // Check root itself if it became empty (only if it's not the main destination root)
                // This is usually handled by the caller if appDest itself needs to be deleted.
            }
            catch (Exception ex) // Catch issues enumerating, e.g. UnauthorizedAccessException on rootDirectory itself
            {
                Debug.WriteLine($"Error during DeleteEmptyDirectories for {rootDirectory}: {ex.Message}");
            }
        }
    }
}
