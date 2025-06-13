using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using BackupWarden.Core.Abstractions.Services.Business;
using System.IO.Abstractions;

namespace BackupWarden.Core.Services.Business
{
    public class FileSystemOperations : IFileSystemOperations
    {
        private static readonly RetryPolicy _retryPolicy = Policy
            .Handle<IOException>()
            .WaitAndRetry(5, retryAttempt => TimeSpan.FromMilliseconds(500 * retryAttempt)); // Changed to synchronous retry

        private readonly ILogger<FileSystemOperations> _logger;
        private readonly IFileSystem _fileSystem; 

        public FileSystemOperations(ILogger<FileSystemOperations> logger, IFileSystem fileSystem)
        {
            _logger = logger;
            _fileSystem = fileSystem;
        }

        public void CopyFile(string sourceFile, string destFile)
        {
            const int bufferSize = 81920; // 80KB
            _retryPolicy.Execute(() =>
            {
                var destDir = _fileSystem.Path.GetDirectoryName(destFile);
                if (destDir != null && !_fileSystem.Directory.Exists(destDir))
                {
                    _fileSystem.Directory.CreateDirectory(destDir);
                }

                using var sourceStream = _fileSystem.FileStream.New(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: false);
                using var destStream = _fileSystem.FileStream.New(destFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: false);
                sourceStream.CopyTo(destStream);
            });
        }

        public void DeleteFile(string filePath)
        {
            _retryPolicy.Execute(() =>
            {
                _fileSystem.File.Delete(filePath);
            });
        }

        public void CreateDirectory(string directoryPath)
        {
            _retryPolicy.Execute(() =>
            {
                _fileSystem.Directory.CreateDirectory(directoryPath);
            });
        }

        public void DeleteEmptyDirectories(string? rootDirectoryPath)
        {
            if (string.IsNullOrEmpty(rootDirectoryPath) || !_fileSystem.Directory.Exists(rootDirectoryPath)) return;
            try
            {
                foreach (var dir in _fileSystem.Directory.EnumerateDirectories(rootDirectoryPath, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length))
                {
                    if (!_fileSystem.Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        try
                        {
                            _retryPolicy.Execute(() =>
                            {
                                _fileSystem.Directory.Delete(dir);
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
            return _fileSystem.File.Exists(filePath);
        }

        public bool DirectoryExists(string? directoryPath)
        {
            return _fileSystem.Directory.Exists(directoryPath);
        }

        public IFileInfo GetFileInfo(string filePath)
        {
            return _fileSystem.FileInfo.New(filePath);
        }

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        {
            return _fileSystem.Directory.EnumerateFiles(path, searchPattern, searchOption);
        }
        
        public void SetLastWriteTimeUtc(string filePath, DateTime lastWriteTimeUtc)
        {
            _retryPolicy.Execute(() =>
            {
                 _fileSystem.File.SetLastWriteTimeUtc(filePath, lastWriteTimeUtc);
            });
        }
    }
}