using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupWarden.Models
{
    public enum SyncMode
    {
        Copy, // Files from the source are copied to the destination. If files with the same names are present on the destination, they are overwritten.
        Sync  // Files on the destination are changed to match those on the source. If a file does not exist on the source, it is also deleted from the destination.
    }

    public enum SyncStatus
    {
        Unknown,            // Initial state or error determining status
        InSync,             // Backup accurately reflects the source application paths.
        OutOfSync,          // Backup does not accurately reflect the source application paths (e.g., source has newer/different files, or backup has files not in source).
        Syncing,            // A backup or restore operation is in progress.
        Failed,             // The last operation failed.
        SourcePathProblem,  // One or more source application paths are empty, inaccessible, or invalid. This can also mean all paths are valid but collectively contain no files.
        BackupPathProblem   // The backup destination for this app is empty (when not expected), inaccessible, or invalid.
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
