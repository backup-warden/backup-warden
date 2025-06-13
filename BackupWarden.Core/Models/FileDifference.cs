using BackupWarden.Core.Models.Extensions;
using System.IO.Abstractions;

namespace BackupWarden.Core.Models
{
    public class FileDifference
    {
        public string RelativePath { get; }
        public FileDifferenceType DifferenceType { get; }
        public IFileInfo? ApplicationFileInfo { get; }
        public IFileInfo? BackupFileInfo { get; }
        public string Description { get; }

        public FileDifference(string relativePath, FileDifferenceType differenceType, string description, IFileInfo? applicationFileInfo = null, IFileInfo? backupFileInfo = null)
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