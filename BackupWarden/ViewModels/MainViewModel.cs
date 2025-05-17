using BackupWarden.Models;
using BackupWarden.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace BackupWarden.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        public ObservableCollection<string> YamlFilePaths { get; } = [];

        private BackupConfig? backupConfig;

        private readonly IYamlConfigService _yamlConfigService;
        private readonly IAppSettingsService _appSettingsService;
        private MainWindow? mainWindow;

        public IRelayCommand AddYamlFileCommand { get; }
        public IRelayCommand LoadConfigCommand { get; }

        public MainViewModel(IAppSettingsService appSettingsService, IYamlConfigService yamlConfigService)
        {
            _yamlConfigService = yamlConfigService;
            _appSettingsService = appSettingsService;

            AddYamlFileCommand = new RelayCommand(async () => await AddYamlFileAsync(), CanAddYamlFile);
            LoadConfigCommand = new RelayCommand(LoadConfig, CanLoadConfig);
            LoadYamlFilePaths();
        }

        private void LoadYamlFilePaths()
        {
            foreach (var path in _appSettingsService.LoadYamlFilePaths())
            {
                YamlFilePaths.Add(path);
            }

            YamlFilePaths.CollectionChanged += (s, e) => _appSettingsService.SaveYamlFilePaths(YamlFilePaths);
        }

        public void SetMainWindow(MainWindow window)
        {
            mainWindow = window;
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

        private void LoadConfig()
        {
            foreach (var path in YamlFilePaths)
            {
                if (!string.IsNullOrEmpty(path))
                {
                    // You may want to merge or handle multiple configs
                    backupConfig = _yamlConfigService.LoadConfig(path);
                }
            }
        }

        private bool CanLoadConfig()
        {
            return YamlFilePaths.Count > 0;
        }

        private bool CanAddYamlFile()
        {
            return mainWindow is not null;
        }
    }
}
