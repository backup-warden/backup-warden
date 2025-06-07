using System.IO;
using BackupWarden.Models.Extensions;
using BackupWarden.Models;

namespace BackupWarden.Models
{
    public class FileDifference
    {
        public string RelativePath { get; }
        public FileDifferenceType DifferenceType { get; }
        public FileInfo? ApplicationFileInfo { get; }
        public FileInfo? BackupFileInfo { get; }
        public string Description { get; }

        public FileDifference(string relativePath, FileDifferenceType differenceType, string description, FileInfo? applicationFileInfo = null, FileInfo? backupFileInfo = null)
        {
            RelativePath = relativePath;
            DifferenceType = differenceType;
            Description = description;
            ApplicationFileInfo = applicationFileInfo;
            BackupFileInfo = backupFileInfo;
        }

        public override string ToString() => $"[{DifferenceType.ToDisplayString()}] {RelativePath}: {Description}";
    }
}