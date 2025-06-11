using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BackupWarden.Core.Models;

namespace BackupWarden.Core.Abstractions.Services.Business
{
    public delegate void AppStatusUpdateCallback(AppConfig app, SyncStatus status, string summaryReport, string detailedReport);

    public interface IBackupSyncService
    {
        Task UpdateSyncStatusAsync(IEnumerable<AppConfig> apps, string backupRoot, AppStatusUpdateCallback? perAppStatusCallback = null);
        Task RestoreAsync(IEnumerable<AppConfig> configs, string backupRoot, SyncMode mode, IProgress<int>? progress = null, AppStatusUpdateCallback? perAppStatusCallback = null);
        Task BackupAsync(IEnumerable<AppConfig> configs, string backupRoot, SyncMode mode, IProgress<int>? progress = null, AppStatusUpdateCallback? perAppStatusCallback = null);
    }
}