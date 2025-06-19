using System.Collections.Generic;
using System.ComponentModel;
using YamlDotNet.Serialization;

namespace BackupWarden.Core.Models
{
    public class AppConfig : INotifyPropertyChanged
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<string> Paths { get; set; } = [];

        private SyncStatus _syncStatus = SyncStatus.Unknown;
        [YamlIgnore]
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

        [YamlIgnore]
        public AppSyncReport? LastSyncReport { get; set; }

        private string? _lastSyncReportSummary;
        [YamlIgnore]
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
        [YamlIgnore]
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