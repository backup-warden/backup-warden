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
        public ObservableCollection<string> YamlFilePaths { get; } = [];
        public ObservableCollection<AppConfig> LoadedApps { get; } = [];
        public ObservableCollection<AppConfig> SelectedApps { get; set; } = [];


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

        private bool _isRestoring;
        public bool IsRestoring
        {
            get => _isRestoring;
            set
            {
                SetProperty(ref _isRestoring, value);
            }
        }

        private int _restoreProgress;
        public int RestoreProgress
        {
            get => _restoreProgress;
            set
            {
                SetProperty(ref _restoreProgress, value);
            }
        }

        private readonly IYamlConfigService _yamlConfigService;
        private readonly IAppSettingsService _appSettingsService;
        private readonly IBackupSyncService _backupSyncService;

        private readonly IPickerService _pickerService;
        private readonly IDialogService _dialogService;

        private readonly ILogger<MainViewModel> _logger;

        private readonly Progress<int> _syncProgressReporter;
        private readonly ContextCallback<AppConfig, SyncStatus> _syncStatusDispatcher;
        private readonly Progress<int> _restoreProgressReporter;


        public IAsyncRelayCommand AddYamlFileCommand { get; }
        public IAsyncRelayCommand SyncCommand { get; }
        public IAsyncRelayCommand BrowseDestinationFolderCommand { get; }
        public IRelayCommand<string> RemoveYamlFileCommand { get; }
        public IAsyncRelayCommand RestoreCommand { get; }

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
            _restoreProgressReporter = new Progress<int>(percent => RestoreProgress = percent);
            _syncStatusDispatcher = new ContextCallback<AppConfig, SyncStatus>((app, status) => app.SyncStatus = status);

            AddYamlFileCommand = new AsyncRelayCommand(AddYamlFileAsync, CanModifySettings);
            RemoveYamlFileCommand = new RelayCommand<string?>(RemoveYamlFile, (_) => CanModifySettings());
            BrowseDestinationFolderCommand = new AsyncRelayCommand(BrowseDestinationFolderAsync, CanModifySettings);
            SyncCommand = new AsyncRelayCommand(SyncAsync, CanSync);
            RestoreCommand = new AsyncRelayCommand(RestoreAsync, CanRestore);

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
                foreach (var app in config.Apps.Where(w => !string.IsNullOrWhiteSpace(w.Id) && w.Paths.Count > 0))
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
            };

            LoadedApps.CollectionChanged += (s, e) =>
            {
                SyncCommand.NotifyCanExecuteChanged();
            };

            SelectedApps.CollectionChanged += (s, e) =>
            {
                RestoreCommand.NotifyCanExecuteChanged();
            };

            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(DestinationFolder))
                {
                    SyncCommand.NotifyCanExecuteChanged();
                    RestoreCommand.NotifyCanExecuteChanged();
                    _ = CheckAppsSyncStatusAsync();
                }
                else if (e.PropertyName == nameof(IsSyncing) || (e.PropertyName == nameof(IsRestoring)))
                {
                    SyncCommand.NotifyCanExecuteChanged();
                    RestoreCommand.NotifyCanExecuteChanged();
                    AddYamlFileCommand.NotifyCanExecuteChanged();
                    BrowseDestinationFolderCommand.NotifyCanExecuteChanged();
                    RemoveYamlFileCommand.NotifyCanExecuteChanged();
                }
            };
        }

        private bool CanSync()
        {
            return !IsSyncing && !IsRestoring && LoadedApps.Count > 0 && !string.IsNullOrWhiteSpace(DestinationFolder);
        }

        private bool CanModifySettings()
        {
            return !IsSyncing && !IsRestoring;
        }

        private bool CanRestore()
        {
            return !IsSyncing && !IsRestoring && SelectedApps.Count > 0 && !string.IsNullOrWhiteSpace(DestinationFolder);
        }

        private async Task CheckAppsSyncStatusAsync()
        {
            if (string.IsNullOrWhiteSpace(DestinationFolder))
            {
                return;
            }
            UpdateSyncStatusToUnknown(LoadedApps);
            await _backupSyncService.UpdateSyncStatusAsync(LoadedApps, DestinationFolder, _syncStatusDispatcher.Invoke);
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
                UpdateSyncStatusToUnknown(LoadedApps);
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

        private async Task RestoreAsync()
        {
            _logger.LogWarning("Restore started.");
            IsRestoring = true;
            RestoreProgress = 0;
            try
            {
                UpdateSyncStatusToUnknown(SelectedApps);
                await _backupSyncService.RestoreAsync(
                    SelectedApps,
                    DestinationFolder!,
                    _restoreProgressReporter, _syncStatusDispatcher.Invoke);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during restore.");
                await _dialogService.ShowErrorAsync("An error occurred during restoration. Please check the logs for details.");
            }
            finally
            {
                IsRestoring = false;
                _logger.LogWarning("Restore finished.");
            }
        }

        private static void UpdateSyncStatusToUnknown(ObservableCollection<AppConfig> appConfigs)
        {
            foreach (var app in appConfigs)
            {
                app.SyncStatus = SyncStatus.Unknown;
            }
        }
    }
}
