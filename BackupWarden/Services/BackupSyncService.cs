using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BackupWarden.Services
{
    public interface IBackupSyncService
    {
        Task SyncAsync(IEnumerable<string> sourcePaths, string destinationRoot);
    }

    public class BackupSyncService : IBackupSyncService
    {
        private static readonly AsyncRetryPolicy RetryPolicy = Policy
            .Handle<IOException>()
            .WaitAndRetryAsync(5, retryAttempt => TimeSpan.FromMilliseconds(500));

        public async Task SyncAsync(IEnumerable<string> sourcePaths, string destinationRoot)
        {
            var sourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var sourcePath in sourcePaths)
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
            }

            // Delete files in destination that are not in source
            foreach (var destFile in Directory.EnumerateFiles(destinationRoot, "*", SearchOption.AllDirectories))
            {
                if (!sourceFiles.Contains(destFile))
                {
                    await Task.Run(() => File.Delete(destFile));
                }
            }
        }

        private static async Task SyncDirectoryAsync(string sourceDir, string destinationRoot, HashSet<string> sourceFiles)
        {
            foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceDir, file);
                var destFile = Path.Combine(destinationRoot, relative);
                await SyncFileInternalAsync(file, destFile, sourceFiles);
            }
        }

        private static async Task SyncFileAsync(string sourceFile, string destinationRoot, HashSet<string> sourceFiles)
        {
            var relative = Path.GetFileName(sourceFile);
            var destFile = Path.Combine(destinationRoot, relative);
            await SyncFileInternalAsync(sourceFile, destFile, sourceFiles);
        }

        private static async Task SyncFileInternalAsync(string sourceFile, string destFile, HashSet<string> sourceFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

            if (!File.Exists(destFile) ||
                new FileInfo(sourceFile).Length != new FileInfo(destFile).Length ||
                File.GetLastWriteTimeUtc(sourceFile) != File.GetLastWriteTimeUtc(destFile))
            {
                await CopyFileAsync(sourceFile, destFile);
            }
            sourceFiles.Add(destFile);
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
