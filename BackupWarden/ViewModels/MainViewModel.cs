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
        public int SelectedAppsCount => SelectedApps.Count;
        public ObservableCollection<AppConfig> SelectedApps { get; set; } = [];

        public ObservableCollection<SyncMode> SyncModes { get; } = [SyncMode.Copy, SyncMode.Sync];

        private SyncMode _selectedSyncMode = SyncMode.Copy;
        public SyncMode SelectedSyncMode
        {
            get => _selectedSyncMode;
            set => SetProperty(ref _selectedSyncMode, value);
        }


        private string? _destinationFolder;

        public string? DestinationFolder
        {
            get => _destinationFolder;
            set
            {
                SetProperty(ref _destinationFolder, value);
            }
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                SetProperty(ref _isRunning, value);
            }
        }

        private int _progress;
        public int Progress
        {
            get => _progress;
            set
            {
                SetProperty(ref _progress, value);
            }
        }

        private readonly IYamlConfigService _yamlConfigService;
        private readonly IAppSettingsService _appSettingsService;
        private readonly IBackupSyncService _backupSyncService;

        private readonly IPickerService _pickerService;
        private readonly IDialogService _dialogService;

        private readonly ILogger<MainViewModel> _logger;

        private readonly Progress<int> _progressReporter;
        private readonly ContextCallback<AppConfig, AppSyncReport> _syncStatusDispatcher;


        public IAsyncRelayCommand AddYamlFileCommand { get; }
        public IAsyncRelayCommand BackupCommand { get; }
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

            _progressReporter = new Progress<int>(percent => Progress = percent);
            _syncStatusDispatcher = new ContextCallback<AppConfig, AppSyncReport>((app, status) =>
            {
                app.SyncStatus = status.OverallStatus;
                app.LastSyncReport = status;
            });

            AddYamlFileCommand = new AsyncRelayCommand(AddYamlFileAsync, CanModifySettings);
            RemoveYamlFileCommand = new RelayCommand<string?>(RemoveYamlFile, (_) => CanModifySettings());
            BrowseDestinationFolderCommand = new AsyncRelayCommand(BrowseDestinationFolderAsync, CanModifySettings);
            BackupCommand = new AsyncRelayCommand(BackupAsync, CanBackup);
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
            SelectedApps.CollectionChanged += (s, e) =>
            {
                BackupCommand.NotifyCanExecuteChanged();
                RestoreCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(SelectedAppsCount));
            };

            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(DestinationFolder))
                {
                    BackupCommand.NotifyCanExecuteChanged();
                    RestoreCommand.NotifyCanExecuteChanged();
                }
                else if (e.PropertyName is nameof(IsRunning))
                {
                    BackupCommand.NotifyCanExecuteChanged();
                    RestoreCommand.NotifyCanExecuteChanged();
                    AddYamlFileCommand.NotifyCanExecuteChanged();
                    BrowseDestinationFolderCommand.NotifyCanExecuteChanged();
                    RemoveYamlFileCommand.NotifyCanExecuteChanged();
                }
            };
        }

        private bool CanBackup()
        {
            return !IsRunning && SelectedApps.Count > 0 && !string.IsNullOrWhiteSpace(DestinationFolder);
        }

        private bool CanModifySettings()
        {
            return !IsRunning;
        }

        private bool CanRestore()
        {
            return !IsRunning && SelectedApps.Count > 0 && !string.IsNullOrWhiteSpace(DestinationFolder);
        }

        private async Task CheckAppsSyncStatusAsync()
        {
            if (string.IsNullOrWhiteSpace(DestinationFolder))
            {
                return;
            }
            try
            {
                IsRunning = true;
                UpdateSyncStatusToUnknown(LoadedApps);
                await _backupSyncService.UpdateSyncStatusAsync(LoadedApps, DestinationFolder, _syncStatusDispatcher.Invoke);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while checking Apps Sync Status.");
                await _dialogService.ShowErrorAsync("An error occurred while checking Apps Sync Status. Please check the logs for details.");
            }
            finally
            {
                IsRunning = false;
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
                    _appSettingsService.SaveYamlFilePaths(YamlFilePaths);
                    LoadAppsFromConfigs();
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
                    _ = CheckAppsSyncStatusAsync();
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
            if (!string.IsNullOrEmpty(filePath) && YamlFilePaths.Remove(filePath))
            {
                _appSettingsService.SaveYamlFilePaths(YamlFilePaths);
                LoadAppsFromConfigs();
            }
        }

        private async Task BackupAsync()
        {
            _logger.LogWarning("Backup started.");
            IsRunning = true;
            Progress = 0;
            try
            {
                UpdateSyncStatusToUnknown(SelectedApps);
                await _backupSyncService.BackupAsync(
                    SelectedApps,
                    DestinationFolder!,
                    SelectedSyncMode,
                    _progressReporter, _syncStatusDispatcher.Invoke);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during backup.");
                await _dialogService.ShowErrorAsync("An error occurred during backup. Please check the logs for details.");
            }
            finally
            {
                IsRunning = false;
                _logger.LogWarning("Backup finished.");
            }
        }

        private async Task RestoreAsync()
        {
            _logger.LogWarning("Restore started.");
            IsRunning = true;
            Progress = 0;
            try
            {
                UpdateSyncStatusToUnknown(SelectedApps);
                await _backupSyncService.RestoreAsync(
                    SelectedApps,
                    DestinationFolder!,
                    SelectedSyncMode,
                    _progressReporter, _syncStatusDispatcher.Invoke);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during restore.");
                await _dialogService.ShowErrorAsync("An error occurred during restoration. Please check the logs for details.");
            }
            finally
            {
                IsRunning = false;
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
