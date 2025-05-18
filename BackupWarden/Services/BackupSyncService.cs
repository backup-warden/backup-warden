using BackupWarden.Models;
using BackupWarden.Utils;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BackupWarden.Services
{
    public interface IBackupSyncService
    {
        Task SyncAsync(IEnumerable<BackupConfig> configs, string destinationRoot, IProgress<int>? progress = null);
    }

    public class BackupSyncService : IBackupSyncService
    {
        private static readonly AsyncRetryPolicy RetryPolicy = Policy
            .Handle<IOException>()
            .WaitAndRetryAsync(5, retryAttempt => TimeSpan.FromMilliseconds(500));

        public async Task SyncAsync(IEnumerable<BackupConfig> configs, string destinationRoot, IProgress<int>? progress = null)
        {
            var appList = configs.SelectMany(cfg => cfg.Apps)
                                 .Where(app => !string.IsNullOrWhiteSpace(app.Id))
                                 .ToList();

            // Calculate total number of paths to sync
            int totalPaths = appList.Sum(app => app.Paths?.Count ?? 0);
            if (totalPaths == 0)
            {
                progress?.Report(100);
                return;
            }

            int processedPaths = 0;

            foreach (var app in appList)
            {
                var appDest = Path.Combine(destinationRoot, app.Id);

                // Report progress after each path
                await SyncPathsAsync(app.Paths, appDest, () =>
                {
                    processedPaths++;
                    int percent = (int)((processedPaths / (double)totalPaths) * 100);
                    progress?.Report(percent);
                });
            }
        }

        private static async Task SyncPathsAsync(IEnumerable<string> sourcePaths, string destinationRoot, Action? onPathProcessed = null)
        {
            var sourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var expandedPaths = sourcePaths.Select(SpecialFolderUtil.ExpandSpecialFolders).ToList();

            try
            {
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
                            await RetryPolicy.ExecuteAsync(() => Task.Run(() => File.Delete(destFile)));
                        }
                    }

                    // Delete empty directories (bottom-up by depth)
                    foreach (var dir in Directory.EnumerateDirectories(destinationRoot, "*", SearchOption.AllDirectories)
                        .OrderByDescending(d => d.Count(c => c == Path.DirectorySeparatorChar)))
                    {
                        if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        {
                            await RetryPolicy.ExecuteAsync(() => Task.Run(() => Directory.Delete(dir)));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                throw;
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
            await RetryPolicy.ExecuteAsync(async () =>
            {
                using var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
                using var destStream = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
                await sourceStream.CopyToAsync(destStream);
            });
        }
    }
}
