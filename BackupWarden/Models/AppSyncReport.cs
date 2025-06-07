using System.Collections.Generic;
using BackupWarden.Models;

namespace BackupWarden.Models
{
    public class AppSyncReport
    {
        public List<PathIssue> PathIssues { get; } = [];
        public List<FileDifference> FileDifferences { get; } = [];
        public SyncStatus OverallStatus { get; set; } = SyncStatus.Unknown;
        public string AppBackupRootPath { get; internal set; } = string.Empty;
    }
}