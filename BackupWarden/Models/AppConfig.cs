using System.Collections.Generic;
using System.ComponentModel;
using BackupWarden.Models;

namespace BackupWarden.Models
{
    public partial class AppConfig : INotifyPropertyChanged
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<string> Paths { get; set; } = [];
        public string? Key { get; set; }
        public string? Account { get; set; }
        public string? Mods { get; set; }

        private SyncStatus _syncStatus = SyncStatus.Unknown;
        public SyncStatus SyncStatus
        {
            get => _syncStatus;
            set
            {
                if (_syncStatus != value)
                {
                    _syncStatus = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SyncStatus)));
                }
            }
        }

        public AppSyncReport? LastSyncReport { get; set; }

        private string? _lastSyncReportSummary;
        public string? LastSyncReportSummary
        {
            get => _lastSyncReportSummary;
            set
            {
                if (_lastSyncReportSummary != value)
                {
                    _lastSyncReportSummary = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastSyncReportSummary)));
                }
            }
        }

        private string? _lastSyncReportDetail;
        public string? LastSyncReportDetail
        {
            get => _lastSyncReportDetail;
            set
            {
                if (_lastSyncReportDetail != value)
                {
                    _lastSyncReportDetail = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastSyncReportDetail)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}