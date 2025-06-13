using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;

namespace BackupWarden.Core.Abstractions.Services.Business
{
    public interface IFileSystemOperations
    {
        void CopyFile(string sourceFile, string destFile);
        void DeleteFile(string filePath);
        void CreateDirectory(string directoryPath);
        void DeleteEmptyDirectories(string? rootDirectoryPath);
        bool FileExists(string? filePath);
        bool DirectoryExists(string? directoryPath);
        IFileInfo GetFileInfo(string filePath);
        IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);
        void SetLastWriteTimeUtc(string filePath, DateTime lastWriteTimeUtc);
    }
}