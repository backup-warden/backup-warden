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
        Task RestoreAsync(IEnumerable<AppConfig> configs, string destinationRoot, IProgress<int>? progress = null, Action<AppConfig, SyncStatus>? perAppStatusCallback = null);
        Task BackupAsync(IEnumerable<AppConfig> configs, string destinationRoot, IProgress<int>? progress = null, Action<AppConfig, SyncStatus>? perAppStatusCallback = null);
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

        public async Task UpdateSyncStatusAsync(
    IEnumerable<AppConfig> apps,
    string destinationRoot,
    Action<AppConfig, SyncStatus>? perAppStatusCallback = null)
        {
            await Task.Run(() =>
            {
                foreach (AppConfig app in apps)
                {
                    try
                    {
                        var appDest = Path.Combine(destinationRoot, app.Id);
                        bool inSync = true;

                        foreach (var sourcePath in app.Paths)
                        {
                            var expandedSource = SpecialFolderUtil.ExpandSpecialFolders(sourcePath);

                            if (expandedSource.EndsWith('\\') || expandedSource.EndsWith('/'))
                            {
                                var dirPath = expandedSource.TrimEnd('\\', '/');
                                if (Directory.Exists(dirPath))
                                {
                                    foreach (var file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories))
                                    {
                                        var relative = GetSpecialFolderRelativePath(file);
                                        var destFile = Path.Combine(appDest, relative);
                                        if (!File.Exists(destFile))
                                        {
                                            inSync = false;
                                            break;
                                        }

                                        var srcInfo = new FileInfo(file);
                                        var dstInfo = new FileInfo(destFile);
                                        if (srcInfo.Length != dstInfo.Length || srcInfo.LastWriteTimeUtc != dstInfo.LastWriteTimeUtc)
                                        {
                                            inSync = false;
                                            break;
                                        }
                                    }
                                }
                            }
                            else if (File.Exists(expandedSource))
                            {
                                var relative = GetSpecialFolderRelativePath(expandedSource);
                                var destFile = Path.Combine(appDest, relative);
                                if (!File.Exists(destFile))
                                {
                                    inSync = false;
                                    break;
                                }

                                var srcInfo = new FileInfo(expandedSource);
                                var dstInfo = new FileInfo(destFile);
                                if (srcInfo.Length != dstInfo.Length || srcInfo.LastWriteTimeUtc != dstInfo.LastWriteTimeUtc)
                                {
                                    inSync = false;
                                    break;
                                }
                            }
                            if (!inSync)
                                break;
                        }

                        perAppStatusCallback?.Invoke(app, inSync ? SyncStatus.InSync : SyncStatus.OutOfSync);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error checking sync status for app {AppId}", app?.Id);
                        perAppStatusCallback?.Invoke(app!, SyncStatus.Failed);
                    }
                }
            });
        }


        public async Task BackupAsync(
        IEnumerable<AppConfig> configs,
        string destinationRoot,
        IProgress<int>? progress = null,
        Action<AppConfig, SyncStatus>? perAppStatusCallback = null)
        {
            await Task.Run(async () =>
            {
                var appList = configs.ToList();
                int totalPaths = appList.Sum(app => app.Paths.Count);
                if (totalPaths == 0)
                {
                    _logger.LogWarning("No paths to sync found in the provided configurations.");
                    progress?.Report(100);
                    return;
                }
                _logger.LogWarning("Total paths to sync: {TotalPaths}", totalPaths);

                int processedPaths = 0;

                foreach (var app in appList)
                {
                    perAppStatusCallback?.Invoke(app, SyncStatus.Syncing);
                    var appDest = Path.Combine(destinationRoot, app.Id);

                    try
                    {
                        await BackupPathsAsync(app.Paths, appDest, () =>
                        {
                            processedPaths++;
                            int percent = (int)(processedPaths / (double)totalPaths * 100);
                            progress?.Report(percent);
                        });

                        perAppStatusCallback?.Invoke(app, SyncStatus.InSync);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error syncing app {AppId}", app.Id);
                        perAppStatusCallback?.Invoke(app, SyncStatus.Failed);
                    }
                }
            });
        }

        private static async Task BackupPathsAsync(IEnumerable<string> sourcePaths, string destinationRoot, Action? onPathProcessed = null)
        {
            var sourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var expandedPaths = sourcePaths.Select(SpecialFolderUtil.ExpandSpecialFolders).ToList();

            foreach (var sourcePath in expandedPaths)
            {
                if (sourcePath.EndsWith('\\') || sourcePath.EndsWith('/'))
                {
                    var dirPath = sourcePath.TrimEnd('\\', '/');
                    if (Directory.Exists(dirPath))
                    {
                        await BackupDirectoryAsync(dirPath, destinationRoot, sourceFiles);
                    }
                }
                else if (File.Exists(sourcePath))
                {
                    await BackupFileAsync(sourcePath, destinationRoot, sourceFiles);
                }
                onPathProcessed?.Invoke();
            }

            // Delete files in destination that are not in source
            if (Directory.Exists(destinationRoot))
            {
                foreach (var destFile in Directory.EnumerateFiles(destinationRoot, "*", SearchOption.AllDirectories))
                {
                    if (!sourceFiles.Contains(destFile))
                    {
                        await _retryPolicy.ExecuteAsync(() => Task.Run(() => File.Delete(destFile)));
                    }
                }

                // Delete empty directories (bottom-up by depth)
                foreach (var dir in Directory.EnumerateDirectories(destinationRoot, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Count(c => c == Path.DirectorySeparatorChar)))
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        await _retryPolicy.ExecuteAsync(() => Task.Run(() => Directory.Delete(dir)));
                    }
                }
            }
        }

        private static async Task BackupDirectoryAsync(string sourceDir, string destinationRoot, HashSet<string> sourceFiles)
        {
            foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = GetSpecialFolderRelativePath(file);
                var destFile = Path.Combine(destinationRoot, relative);
                await BackupFileInternalAsync(file, destFile, sourceFiles);
            }
        }

        private static async Task BackupFileAsync(string sourceFile, string destinationRoot, HashSet<string> sourceFiles)
        {
            var relative = GetSpecialFolderRelativePath(sourceFile);
            var destFile = Path.Combine(destinationRoot, relative);
            await BackupFileInternalAsync(sourceFile, destFile, sourceFiles);
        }

        private static async Task BackupFileInternalAsync(string sourceFile, string destFile, HashSet<string> sourceFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

            var fileInfoSource = new FileInfo(sourceFile);
            var fileInfoDestination = File.Exists(destFile) ? new FileInfo(destFile) : null;

            if (fileInfoDestination is null ||
                fileInfoSource.Length != fileInfoDestination.Length ||
                fileInfoSource.LastWriteTimeUtc != fileInfoDestination.LastWriteTimeUtc)
            {
                await CopyFileAsync(sourceFile, destFile);
                File.SetLastWriteTimeUtc(destFile, fileInfoSource.LastWriteTimeUtc);
            }
            sourceFiles.Add(destFile);
        }

        public async Task RestoreAsync(
IEnumerable<AppConfig> configs,
string destinationRoot,
IProgress<int>? progress = null,
Action<AppConfig, SyncStatus>? perAppStatusCallback = null)
        {
            await Task.Run(async () =>
            {
                var appList = configs.ToList();
                int totalFiles = 0;
                var appFileMap = new Dictionary<AppConfig, List<string>>();

                // Precompute expanded and normalized paths for each app
                var appPathMap = new Dictionary<AppConfig, List<(string Path, bool IsDirectory)>>();

                foreach (var app in appList)
                {
                    var expandedPaths = app.Paths
                        .Select(SpecialFolderUtil.ExpandSpecialFolders)
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .Select(p => (
                            Path: Path.GetFullPath(p.TrimEnd('\\', '/')),
                            IsDirectory: p.EndsWith('\\') || p.EndsWith('/')
                        ))
                        .ToList();
                    appPathMap[app] = expandedPaths;
                }

                foreach (var app in appList)
                {
                    var appBackupRoot = Path.Combine(destinationRoot, app.Id);
                    var files = new List<string>();
                    if (Directory.Exists(appBackupRoot))
                    {
                        files = [.. Directory.EnumerateFiles(appBackupRoot, "*", SearchOption.AllDirectories)];
                    }
                    appFileMap[app] = files;
                    totalFiles += files.Count;
                }

                if (totalFiles == 0)
                {
                    _logger.LogWarning("No files to restore found in the provided backup directories.");
                    progress?.Report(100);
                    return;
                }
                _logger.LogWarning("Total files to restore: {TotalFiles}", totalFiles);

                int processedFiles = 0;

                foreach (var app in appList)
                {
                    perAppStatusCallback?.Invoke(app, SyncStatus.Syncing);
                    var appBackupRoot = Path.Combine(destinationRoot, app.Id);
                    var files = appFileMap[app];
                    var mappedPaths = appPathMap[app];

                    try
                    {
                        foreach (var backupFile in files)
                        {
                            var relativePathFromBackup = Path.GetRelativePath(appBackupRoot, backupFile);

                            var pathAfterUserMapping = SpecialFolderUtil.MapBackupUserPathToCurrentUser(relativePathFromBackup);

                            
                            var expandedFinalPathCandidate = SpecialFolderUtil.ExpandSpecialFolders(pathAfterUserMapping);

                            
                            string destFile = Path.GetFullPath(expandedFinalPathCandidate);

                            // Only restore if destFile is under one of the mapped paths
                            bool shouldRestore = mappedPaths.Any(mp =>
                                mp.IsDirectory
                                    ? destFile.StartsWith(mp.Path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                                    : string.Equals(destFile, mp.Path, StringComparison.OrdinalIgnoreCase)
                            );

                            if (!shouldRestore)
                            {
                                continue;
                            }

                            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

                            await RestoreFileInternalAsync(backupFile, destFile);

                            processedFiles++;
                            int percent = (int)(processedFiles / (double)totalFiles * 100);
                            progress?.Report(percent);
                        }

                        perAppStatusCallback?.Invoke(app, SyncStatus.InSync);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error restoring app {AppId}", app.Id);
                        perAppStatusCallback?.Invoke(app, SyncStatus.Failed);
                    }
                }
            });
        }

        private static async Task RestoreFileInternalAsync(string backupFile, string destFile)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

            if (File.Exists(backupFile))
            {
                await CopyFileAsync(backupFile, destFile);
                var backupInfo = new FileInfo(backupFile);
                File.SetLastWriteTimeUtc(destFile, backupInfo.LastWriteTimeUtc);
            }
        }

        private static string GetDriveLetterRelativePath(string fullPath)
        {
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root) || root.Length < 2)
                return fullPath;

            // Remove the colon and backslash (e.g., "C:\") and prepend the drive letter with a separator
            var driveLetter = root[0].ToString();
            var rest = fullPath[root.Length..];
            return Path.Combine(driveLetter, rest);
        }

        private static string GetSpecialFolderRelativePath(string fullPath)
        {
            // First try to convert to special folder path
            var specialPath = SpecialFolderUtil.ConvertToSpecialFolderPath(fullPath);

            // If the conversion didn't result in a special folder path, fall back to drive letter path
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
    }
}
