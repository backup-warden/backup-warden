using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BackupWarden.Services.UI
{
    public interface IPickerService
    {
        Task<IReadOnlyList<string>> PickFilesAsync(IEnumerable<string> fileTypeFilters, bool allowMultiple = false);
        Task<string?> PickFolderAsync();
    }

    public class PickerService : IPickerService
    {
        public async Task<IReadOnlyList<string>> PickFilesAsync(IEnumerable<string> fileTypeFilters, bool allowMultiple = false)
        {
            var picker = new FileOpenPicker();
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            foreach (var filter in fileTypeFilters)
                picker.FileTypeFilter.Add(filter);

            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.ViewMode = PickerViewMode.List;

            if (allowMultiple)
            {
                var files = await picker.PickMultipleFilesAsync();
                List<string> result = [];
                if (files is not null)
                {
                    foreach (var file in files)
                        result.Add(file.Path);
                }
                return result;
            }
            else
            {
                var file = await picker.PickSingleFileAsync();
                return file is not null ? [file.Path] : [];
            }
        }

        public async Task<string?> PickFolderAsync()
        {
            var picker = new FolderPicker();
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }
    }
}
