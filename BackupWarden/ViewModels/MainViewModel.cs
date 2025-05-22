using BackupWarden.Models;
using BackupWarden.Services.Business;
using BackupWarden.Services.UI;
using BackupWarden.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BackupWarden.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        public ObservableCollection<AppConfig> LoadedApps { get; } = [];

        public ObservableCollection<string> YamlFilePaths { get; } = [];

        private string? _destinationFolder;

        public string? DestinationFolder
        {
            get => _destinationFolder;
            set
            {
                SetProperty(ref _destinationFolder, value);
            }
        }

        private bool _isSyncing;
        public bool IsSyncing
        {
            get => _isSyncing;
            set
            {
                SetProperty(ref _isSyncing, value);
            }
        }

        private int _syncProgress;
        public int SyncProgress
        {
            get => _syncProgress;
            set
            {
                SetProperty(ref _syncProgress, value);
            }
        }

        public ObservableCollection<AppConfig> SelectedApps { get; set; } = [];


        private readonly IYamlConfigService _yamlConfigService;
        private readonly IAppSettingsService _appSettingsService;
        private readonly IBackupSyncService _backupSyncService;

        private readonly IPickerService _pickerService;
        private readonly IDialogService _dialogService;

        private readonly ILogger<MainViewModel> _logger;

        private readonly Progress<int> _syncProgressReporter;
        private readonly ContextCallback<AppConfig, SyncStatus> _syncStatusDispatcher;


        public IAsyncRelayCommand AddYamlFileCommand { get; }
        public IAsyncRelayCommand SyncCommand { get; }
        public IAsyncRelayCommand BrowseDestinationFolderCommand { get; }
        public IRelayCommand<string> RemoveYamlFileCommand { get; }

        public MainViewModel(
            IAppSettingsService appSettingsService,
            IYamlConfigService yamlConfigService,
            IBackupSyncService backupSyncService,
            IPickerService pickerService,
            IDialogService dialogService,
            ILogger<MainViewModel> logger)
        {
            _yamlConfigService = yamlConfigService;
            _appSettingsService = appSettingsService;
            _backupSyncService = backupSyncService;

            _pickerService = pickerService;
            _dialogService = dialogService;
            _logger = logger;

            _syncProgressReporter = new Progress<int>(percent => SyncProgress = percent);
            _syncStatusDispatcher = new ContextCallback<AppConfig, SyncStatus>((app, status) => app.SyncStatus = status);

            AddYamlFileCommand = new AsyncRelayCommand(AddYamlFileAsync);
            BrowseDestinationFolderCommand = new AsyncRelayCommand(BrowseDestinationFolderAsync);
            SyncCommand = new AsyncRelayCommand(SyncAsync, CanSync);
            RemoveYamlFileCommand = new RelayCommand<string>(RemoveYamlFile);

            LoadAppSettings();
            LoadAppsFromConfigs();
            MonitorPropertyChanged();

        }

        private void LoadAppSettings()
        {
            DestinationFolder = _appSettingsService.LoadDestinationFolder();
            foreach (var path in _appSettingsService.LoadYamlFilePaths())
            {
                YamlFilePaths.Add(path);
            }
        }

        private void LoadAppsFromConfigs()
        {
            LoadedApps.Clear();
            foreach (var config in YamlFilePaths
                .Where(File.Exists)
                .Select(_yamlConfigService.LoadConfig)
                .Where(cfg => cfg is not null))
            {
                foreach (var app in config.Apps.Where(w => w.Paths.Count > 0))
                {
                    LoadedApps.Add(app);
                }
            }
            _ = CheckAppsSyncStatusAsync();
        }

        private void MonitorPropertyChanged()
        {
            YamlFilePaths.CollectionChanged += (s, e) =>
            {
                _appSettingsService.SaveYamlFilePaths(YamlFilePaths);
                LoadAppsFromConfigs();
                SyncCommand.NotifyCanExecuteChanged();
            };

            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(DestinationFolder))
                {
                    SyncCommand.NotifyCanExecuteChanged();
                    _ = CheckAppsSyncStatusAsync();
                }
                else if (e.PropertyName == nameof(IsSyncing))
                {
                    SyncCommand.NotifyCanExecuteChanged();
                }
            };
        }

        private async Task CheckAppsSyncStatusAsync()
        {
            if (string.IsNullOrWhiteSpace(DestinationFolder))
            {
                return;
            }
            foreach (var app in LoadedApps)
            {
                await Task.Run(() =>
                {
                    var status = _backupSyncService.IsAppInSync(app, DestinationFolder)
                       ? SyncStatus.InSync
                       : SyncStatus.OutOfSync;
                    _syncStatusDispatcher.Invoke(app, status);
                });
            }
        }

        private async Task AddYamlFileAsync()
        {
            try
            {
                var yamlFiles = await _pickerService.PickFilesAsync([".yaml", ".yml"], allowMultiple: true);
                if (yamlFiles is not null)
                {
                    foreach (var file in yamlFiles)
                    {
                        if (!YamlFilePaths.Contains(file))
                        {
                            YamlFilePaths.Add(file);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while adding YAML files.");
                await _dialogService.ShowErrorAsync("An error occurred. Please check the logs for details.");
            }
        }

        private bool CanSync()
        {
            return !IsSyncing && YamlFilePaths.Count > 0 && !string.IsNullOrWhiteSpace(DestinationFolder);
        }

        private async Task BrowseDestinationFolderAsync()
        {
            try
            {
                var folder = await _pickerService.PickFolderAsync();
                if (folder is not null)
                {
                    DestinationFolder = folder;
                    _appSettingsService.SaveDestinationFolder(folder);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while browsing for a destination folder.");
                await _dialogService.ShowErrorAsync("An error occurred. Please check the logs for details.");
            }
        }

        private void RemoveYamlFile(string? filePath)
        {
            if (!string.IsNullOrEmpty(filePath) && YamlFilePaths.Contains(filePath))
            {
                YamlFilePaths.Remove(filePath);
            }
        }

        private async Task SyncAsync()
        {
            _logger.LogWarning("Sync started.");
            IsSyncing = true;
            SyncProgress = 0;
            try
            {
                await _backupSyncService.SyncAsync(
                    LoadedApps,
                    DestinationFolder!,
                    _syncProgressReporter, _syncStatusDispatcher.Invoke);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during sync.");
                await _dialogService.ShowErrorAsync("An error occurred during synchronization. Please check the logs for details.");
            }
            finally
            {
                IsSyncing = false;
                _logger.LogWarning("Sync finished.");
            }
        }
    }
}
