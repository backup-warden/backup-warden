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
        bool IsAppInSync(AppConfig app, string destinationRoot);

        Task SyncAsync(IEnumerable<AppConfig> configs, string destinationRoot, IProgress<int>? progress = null, Action<AppConfig, SyncStatus>? perAppStatusCallback = null);
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

        public bool IsAppInSync(AppConfig app, string destinationRoot)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(app.Id))
                {
                    return false;
                }

                var appDest = Path.Combine(destinationRoot, app.Id);

                foreach (var sourcePath in app.Paths)
                {
                    var expandedSource = SpecialFolderUtil.ExpandSpecialFolders(sourcePath);
                    if (Directory.Exists(expandedSource))
                    {
                        foreach (var file in Directory.EnumerateFiles(expandedSource, "*", SearchOption.AllDirectories))
                        {
                            var relative = GetDriveLetterRelativePath(file);
                            var destFile = Path.Combine(appDest, relative);
                            if (!File.Exists(destFile))
                            {
                                return false;
                            }

                            var srcInfo = new FileInfo(file);
                            var dstInfo = new FileInfo(destFile);
                            if (srcInfo.Length != dstInfo.Length || srcInfo.LastWriteTimeUtc != dstInfo.LastWriteTimeUtc)
                            {
                                return false;
                            }
                        }
                    }
                    else if (File.Exists(expandedSource))
                    {
                        var relative = GetDriveLetterRelativePath(expandedSource);
                        var destFile = Path.Combine(appDest, relative);
                        if (!File.Exists(destFile))
                        {
                            return false;
                        }

                        var srcInfo = new FileInfo(expandedSource);
                        var dstInfo = new FileInfo(destFile);
                        if (srcInfo.Length != dstInfo.Length || srcInfo.LastWriteTimeUtc != dstInfo.LastWriteTimeUtc)
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking sync status for app {AppId}", app?.Id);
                return false;
            }
        }


        public async Task SyncAsync(
        IEnumerable<AppConfig> configs,
        string destinationRoot,
        IProgress<int>? progress = null,
        Action<AppConfig, SyncStatus>? perAppStatusCallback = null)
        {
            var appList = configs.Where(app => !string.IsNullOrWhiteSpace(app.Id)).ToList();
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
                    await SyncPathsAsync(app.Paths, appDest, () =>
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
        }

        private static async Task SyncPathsAsync(IEnumerable<string> sourcePaths, string destinationRoot, Action? onPathProcessed = null)
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
                        await SyncDirectoryAsync(dirPath, destinationRoot, sourceFiles);
                    }
                }
                else if (File.Exists(sourcePath))
                {
                    await SyncFileAsync(sourcePath, destinationRoot, sourceFiles);
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

        private static async Task SyncDirectoryAsync(string sourceDir, string destinationRoot, HashSet<string> sourceFiles)
        {
            foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = GetDriveLetterRelativePath(file);
                var destFile = Path.Combine(destinationRoot, relative);
                await SyncFileInternalAsync(file, destFile, sourceFiles);
            }
        }

        private static async Task SyncFileAsync(string sourceFile, string destinationRoot, HashSet<string> sourceFiles)
        {
            var relative = GetDriveLetterRelativePath(sourceFile);
            var destFile = Path.Combine(destinationRoot, relative);
            await SyncFileInternalAsync(sourceFile, destFile, sourceFiles);
        }

        private static async Task SyncFileInternalAsync(string sourceFile, string destFile, HashSet<string> sourceFiles)
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
            var appList = configs.Where(app => !string.IsNullOrWhiteSpace(app.Id)).ToList();
            int totalPaths = appList.Sum(app => app.Paths.Count);
            if (totalPaths == 0)
            {
                _logger.LogWarning("No paths to restore found in the provided configurations.");
                progress?.Report(100);
                return;
            }
            _logger.LogWarning("Total paths to restore: {TotalPaths}", totalPaths);

            int processedPaths = 0;

            foreach (var app in appList)
            {
                perAppStatusCallback?.Invoke(app, SyncStatus.Syncing);
                var appDest = Path.Combine(destinationRoot, app.Id);

                try
                {
                    await RestorePathsAsync(app.Paths, appDest, () =>
                    {
                        processedPaths++;
                        int percent = (int)(processedPaths / (double)totalPaths * 100);
                        progress?.Report(percent);
                    });

                    perAppStatusCallback?.Invoke(app, SyncStatus.InSync);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error restoring app {AppId}", app.Id);
                    perAppStatusCallback?.Invoke(app, SyncStatus.Failed);
                }
            }
        }

        private static async Task RestorePathsAsync(IEnumerable<string> sourcePaths, string backupRoot, Action? onPathProcessed = null)
        {
            var expandedPaths = sourcePaths.Select(SpecialFolderUtil.ExpandSpecialFolders).ToList();

            foreach (var sourcePath in expandedPaths)
            {
                if (sourcePath.EndsWith('\\') || sourcePath.EndsWith('/'))
                {
                    var dirPath = sourcePath.TrimEnd('\\', '/');
                    await RestoreDirectoryAsync(dirPath, backupRoot);
                }
                else
                {
                    await RestoreFileAsync(sourcePath, backupRoot);
                }
                onPathProcessed?.Invoke();
            }
        }

        private static async Task RestoreDirectoryAsync(string sourceDir, string backupRoot)
        {
            // The backup directory is under backupRoot\<AppId>\<DriveLetter>\<relative path>
            var driveLetter = Path.GetPathRoot(sourceDir)?[0].ToString();
            var relativeDir = GetDriveLetterRelativePath(sourceDir);
            var backupDir = Path.Combine(backupRoot, relativeDir);

            if (Directory.Exists(backupDir))
            {
                foreach (var file in Directory.EnumerateFiles(backupDir, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(backupDir, file);
                    var destFile = Path.Combine(sourceDir, relative);
                    await RestoreFileInternalAsync(file, destFile);
                }
            }
        }

        private static async Task RestoreFileAsync(string sourceFile, string backupRoot)
        {
            var relative = GetDriveLetterRelativePath(sourceFile);
            var backupFile = Path.Combine(backupRoot, relative);
            await RestoreFileInternalAsync(backupFile, sourceFile);
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
