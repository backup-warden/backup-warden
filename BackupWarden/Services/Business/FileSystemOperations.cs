using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace BackupWarden.Services.Business
{
    public class FileSystemOperations : IFileSystemOperations
    {
        private static readonly RetryPolicy _retryPolicy = Policy
            .Handle<IOException>()
            .WaitAndRetry(5, retryAttempt => TimeSpan.FromMilliseconds(500 * retryAttempt)); // Changed to synchronous retry

        private readonly ILogger<FileSystemOperations> _logger;

        public FileSystemOperations(ILogger<FileSystemOperations> logger)
        {
            _logger = logger;
        }

        public void CopyFile(string sourceFile, string destFile)
        {
            const int bufferSize = 81920; // 80KB
            _retryPolicy.Execute(() =>
            {
                var destDir = Path.GetDirectoryName(destFile);
                if (destDir != null && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                using var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: false); // useAsync: false
                using var destStream = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: false); // useAsync: false
                sourceStream.CopyTo(destStream); // Synchronous copy
            });
        }

        public void DeleteFile(string filePath)
        {
            _retryPolicy.Execute(() =>
            {
                File.Delete(filePath);
            });
        }

        public void CreateDirectory(string directoryPath)
        {
            _retryPolicy.Execute(() =>
            {
                Directory.CreateDirectory(directoryPath);
            });
        }

        public void DeleteEmptyDirectories(string? rootDirectoryPath)
        {
            if (string.IsNullOrEmpty(rootDirectoryPath) || !Directory.Exists(rootDirectoryPath)) return; // Added null/empty check
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(rootDirectoryPath, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length))
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        try
                        {
                            _retryPolicy.Execute(() =>
                            {
                                Directory.Delete(dir);
                                _logger.LogInformation("Deleted empty directory {Directory}", dir);
                            });
                        }
                        catch (IOException ex)
                        {
                            _logger.LogWarning(ex, "Could not delete empty directory {Directory} (possibly in use or timing issue).", dir);
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            _logger.LogWarning(ex, "Access denied deleting empty directory {Directory}.", dir);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during DeleteEmptyDirectories for {RootDirectory}", rootDirectoryPath);
            }
        }

        public bool FileExists(string? filePath)
        {
            return File.Exists(filePath);
        }

        public bool DirectoryExists(string? directoryPath)
        {
            return Directory.Exists(directoryPath);
        }

        public FileInfo GetFileInfo(string filePath)
        {
            return new FileInfo(filePath);
        }

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        {
            return Directory.EnumerateFiles(path, searchPattern, searchOption);
        }
        
        public void SetLastWriteTimeUtc(string filePath, DateTime lastWriteTimeUtc)
        {
            _retryPolicy.Execute(() =>
            {
                 File.SetLastWriteTimeUtc(filePath, lastWriteTimeUtc);
            });
        }
    }
}