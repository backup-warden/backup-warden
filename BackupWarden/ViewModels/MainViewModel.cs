using BackupWarden.Models;
using BackupWarden.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        private MainWindow? mainWindow;

        public IRelayCommand AddYamlFileCommand { get; }
        public IRelayCommand SyncCommand { get; }
        public IRelayCommand BrowseDestinationFolderCommand { get; }

        public MainViewModel(
            IAppSettingsService appSettingsService,
            IYamlConfigService yamlConfigService,
            IBackupSyncService backupSyncService)
        {
            _yamlConfigService = yamlConfigService;
            _appSettingsService = appSettingsService;
            _backupSyncService = backupSyncService;

            AddYamlFileCommand = new RelayCommand(async () => await AddYamlFileAsync(), IsMainWindowSetted);
            BrowseDestinationFolderCommand = new RelayCommand(async () => await BrowseDestinationFolderAsync(), IsMainWindowSetted);
            SyncCommand = new RelayCommand(async () => await SyncAsync(), CanSync);

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

        public void SetMainWindow(MainWindow window)
        {
            mainWindow = window;
            AddYamlFileCommand.NotifyCanExecuteChanged();
            BrowseDestinationFolderCommand.NotifyCanExecuteChanged();
        }

        private async Task AddYamlFileAsync()
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
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

        private bool IsMainWindowSetted()
        {
            return mainWindow is not null;
        }

        private bool CanSync()
        {
            return !IsSyncing && YamlFilePaths.Count > 0 && !string.IsNullOrWhiteSpace(DestinationFolder);
        }

        private async Task BrowseDestinationFolderAsync()
        {
            if (mainWindow is null)
                return;

            var picker = new Windows.Storage.Pickers.FolderPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
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

        private async Task SyncAsync()
        {
            IsSyncing = true;
            SyncProgress = 0;
            try
            {
                // Load all configs
                var configs = YamlFilePaths
                    .Where(File.Exists)
                    .Select(path => _yamlConfigService.LoadConfig(path))
                    .Where(cfg => cfg is not null)
                    .ToList();

                var progress = new Progress<int>(percent =>
                {
                    SyncProgress = percent;
                });

                await _backupSyncService.SyncAsync(configs, DestinationFolder!, progress);
            }
            finally
            {
                IsSyncing = false;
            }
        }
    }
}
