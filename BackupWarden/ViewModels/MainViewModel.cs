using BackupWarden.Models;
using BackupWarden.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BackupWarden.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        public ObservableCollection<string> YamlFilePaths { get; } = [];

        private string? _destinationFolder;

        public string? DestinationFolder
        {
            get => _destinationFolder;
            set
            {
                SetProperty(ref _destinationFolder, value);
                SyncCommand.NotifyCanExecuteChanged();
            }
        }

        private bool _isSyncing;
        public bool IsSyncing
        {
            get => _isSyncing;
            set
            {
                SetProperty(ref _isSyncing, value);
                SyncCommand.NotifyCanExecuteChanged();
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

        private readonly IYamlConfigService _yamlConfigService;
        private readonly IAppSettingsService _appSettingsService;
        private readonly IBackupSyncService _backupSyncService;
        private readonly ILogger<MainViewModel> _logger;

        public IAsyncRelayCommand AddYamlFileCommand { get; }
        public IAsyncRelayCommand SyncCommand { get; }
        public IAsyncRelayCommand BrowseDestinationFolderCommand { get; }
        public IRelayCommand<string> RemoveYamlFileCommand { get; }

        public MainViewModel(
            IAppSettingsService appSettingsService,
            IYamlConfigService yamlConfigService,
            IBackupSyncService backupSyncService,
            ILogger<MainViewModel> logger)
        {
            _yamlConfigService = yamlConfigService;
            _appSettingsService = appSettingsService;
            _backupSyncService = backupSyncService;
            _logger = logger;

            AddYamlFileCommand = new AsyncRelayCommand(AddYamlFileAsync);
            BrowseDestinationFolderCommand = new AsyncRelayCommand(BrowseDestinationFolderAsync);
            SyncCommand = new AsyncRelayCommand(SyncAsync, CanSync);
            RemoveYamlFileCommand = new RelayCommand<string>(RemoveYamlFile);

            LoadAppSettings();

        }

        private void LoadAppSettings()
        {
            DestinationFolder = _appSettingsService.LoadDestinationFolder();
            foreach (var path in _appSettingsService.LoadYamlFilePaths())
            {
                YamlFilePaths.Add(path);
            }

            YamlFilePaths.CollectionChanged += (s, e) =>
            {
                _appSettingsService.SaveYamlFilePaths(YamlFilePaths);
                SyncCommand.NotifyCanExecuteChanged();
            };
        }

        private async Task AddYamlFileAsync()
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                picker.FileTypeFilter.Add(".yaml");
                picker.FileTypeFilter.Add(".yml");
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;

                var files = await picker.PickMultipleFilesAsync();
                if (files is not null)
                {
                    foreach (var file in files)
                    {
                        if (!YamlFilePaths.Contains(file.Path))
                        {
                            YamlFilePaths.Add(file.Path);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while adding YAML files.");
                await ShowErrorAsync("An error occurred. Please check the logs for details.");
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
                var picker = new Windows.Storage.Pickers.FolderPicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                picker.FileTypeFilter.Add("*");

                var folder = await picker.PickSingleFolderAsync();
                if (folder is not null)
                {
                    DestinationFolder = folder.Path;
                    _appSettingsService.SaveDestinationFolder(folder.Path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while browsing for a destination folder.");
                await ShowErrorAsync("An error occurred. Please check the logs for details.");
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
                // Load all configs
                var configs = YamlFilePaths
                    .Where(File.Exists)
                    .Select(_yamlConfigService.LoadConfig)
                    .Where(cfg => cfg is not null)
                    .ToList();

                var progress = new Progress<int>(percent =>
                {
                    SyncProgress = percent;
                });

                await _backupSyncService.SyncAsync(configs, DestinationFolder!, progress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during sync.");

                await ShowErrorAsync("An error occurred during synchronization. Please check the logs for details.");
            }
            finally
            {
                IsSyncing = false;
                _logger.LogWarning("Sync finished.");
            }
        }

        private static async Task ShowErrorAsync(string message)
        {
            var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
            {
                Title = "Error",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = App.MainWindow.Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
