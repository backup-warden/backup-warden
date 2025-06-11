using BackupWarden.Core.Abstractions.Services.Business;
using BackupWarden.Core.Abstractions.Services.UI;
using BackupWarden.Core.Models;
using BackupWarden.Core.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BackupWarden.Core.ViewModels
{
    public class MainViewModel : ObservableObject
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


        private string? _backupFolder;

        public string? BackupFolder
        {
            get => _backupFolder;
            set
            {
                SetProperty(ref _backupFolder, value);
            }
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set => SetProperty(ref _isRunning, value);
        }

        private bool _isCheckingSyncStatus;
        public bool IsCheckingSyncStatus
        {
            get => _isCheckingSyncStatus;
            set => SetProperty(ref _isCheckingSyncStatus, value);
        }

        public bool ShowProgressBar => IsRunning && !IsCheckingSyncStatus;

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
        private readonly ContextCallback<AppConfig, SyncStatus, string, string> _syncStatusDispatcher;


        public IAsyncRelayCommand AddYamlFileCommand { get; }
        public IAsyncRelayCommand BackupCommand { get; }
        public IAsyncRelayCommand BrowseBackupFolderCommand { get; }
        public IRelayCommand<string> RemoveYamlFileCommand { get; }
        public IAsyncRelayCommand RestoreCommand { get; }
        public IAsyncRelayCommand RefreshSyncStatusCommand { get; }

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
            _syncStatusDispatcher = new ContextCallback<AppConfig, SyncStatus, string, string>((app, syncStatus, lastSyncReportSummary, lastSyncReportDetail) =>
            {
                app.SyncStatus = syncStatus;
                app.LastSyncReportSummary = lastSyncReportSummary;
                app.LastSyncReportDetail = lastSyncReportDetail;
            });

            AddYamlFileCommand = new AsyncRelayCommand(AddYamlFileAsync, CanModifySettings);
            RemoveYamlFileCommand = new RelayCommand<string?>(RemoveYamlFile, (_) => CanModifySettings());
            BrowseBackupFolderCommand = new AsyncRelayCommand(BrowseBackupFolderAsync, CanModifySettings);
            BackupCommand = new AsyncRelayCommand(BackupAsync, CanBackup);
            RestoreCommand = new AsyncRelayCommand(RestoreAsync, CanRestore);
            RefreshSyncStatusCommand = new AsyncRelayCommand(CheckAppsSyncStatusAsync, CanModifySettings);

            LoadAppSettings();
            LoadAppsFromConfigs();
            MonitorPropertyChanged();

        }

        private void LoadAppSettings()
        {
            BackupFolder = _appSettingsService.LoadBackupFolder();
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
                if (e.PropertyName is nameof(BackupFolder))
                {
                    BackupCommand.NotifyCanExecuteChanged();
                    RestoreCommand.NotifyCanExecuteChanged();
                }
                else if (e.PropertyName is nameof(IsRunning) || e.PropertyName is nameof(IsCheckingSyncStatus))
                {
                    OnPropertyChanged(nameof(ShowProgressBar));
                    if (e.PropertyName is nameof(IsRunning))
                    {
                        BackupCommand.NotifyCanExecuteChanged();
                        RestoreCommand.NotifyCanExecuteChanged();
                        AddYamlFileCommand.NotifyCanExecuteChanged();
                        BrowseBackupFolderCommand.NotifyCanExecuteChanged();
                        RemoveYamlFileCommand.NotifyCanExecuteChanged();
                        RefreshSyncStatusCommand.NotifyCanExecuteChanged();
                    }
                }
            };
        }

        private bool CanBackup()
        {
            return !IsRunning && SelectedApps.Count > 0 && !string.IsNullOrWhiteSpace(BackupFolder);
        }

        private bool CanModifySettings()
        {
            return !IsRunning;
        }

        private bool CanRestore()
        {
            return !IsRunning && SelectedApps.Count > 0 && !string.IsNullOrWhiteSpace(BackupFolder);
        }

        private async Task CheckAppsSyncStatusAsync()
        {
            if (string.IsNullOrWhiteSpace(BackupFolder))
            {
                return;
            }
            try
            {
                IsRunning = true;
                IsCheckingSyncStatus = true;
                UpdateSyncStatusToUnknown(LoadedApps);
                await _backupSyncService.UpdateSyncStatusAsync(LoadedApps, BackupFolder, _syncStatusDispatcher.Invoke);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while checking Apps Sync Status.");
                await _dialogService.ShowErrorAsync("An error occurred while checking Apps Sync Status. Please check the logs for details.");
            }
            finally
            {
                IsCheckingSyncStatus = false;
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

        private async Task BrowseBackupFolderAsync()
        {
            try
            {
                var folder = await _pickerService.PickFolderAsync();
                if (folder is not null)
                {
                    BackupFolder = folder;
                    _appSettingsService.SaveBackupFolder(folder);
                    _ = CheckAppsSyncStatusAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while browsing for a backup folder.");
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
                    BackupFolder!,
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
                    BackupFolder!,
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