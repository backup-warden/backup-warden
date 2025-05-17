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
        private readonly MainWindow _mainWindow;
        public IRelayCommand AddYamlFileCommand { get; }
        public IRelayCommand LoadConfigCommand { get; }

        public MainViewModel(MainWindow mainWindow)
        {
            AddYamlFileCommand = new RelayCommand(async () => await AddYamlFileAsync());
            LoadConfigCommand = new RelayCommand(LoadConfig, CanLoadConfig);
            _mainWindow = mainWindow;
        }

        private async Task AddYamlFileAsync()
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.FileTypeFilter.Add(".yaml");
            picker.FileTypeFilter.Add(".yml");
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;

            var files = await picker.PickMultipleFilesAsync();
            if (files != null)
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
                    backupConfig = YamlConfigService.LoadConfig(path);
                }
            }
        }

        private bool CanLoadConfig()
        {
            return YamlFilePaths.Count > 0;
        }
    }
}
