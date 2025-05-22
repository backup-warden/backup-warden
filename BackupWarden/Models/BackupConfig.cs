using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupWarden.Models
{
    public enum SyncStatus
    {
        Unknown,
        InSync,
        OutOfSync,
        Syncing,
        Failed
    }

    public class BackupConfig
    {
        public List<AppConfig> Apps { get; set; } = [];
    }

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

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
