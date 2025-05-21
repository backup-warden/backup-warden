using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

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

        //private unsafe void myButton_Click(object sender, RoutedEventArgs e)
        //{
        //    // Retrieve the window handle (HWND) of the main WinUI 3 window.
        //    var hWnd = WindowNative.GetWindowHandle(App.MainWindow);

        //    int hr = PInvoke.CoCreateInstance<IFileSaveDialog>(
        //        typeof(FileSaveDialog).GUID,
        //        null,
        //        CLSCTX.CLSCTX_INPROC_SERVER,
        //        out var fsd);
        //    if (hr < 0)
        //    {
        //        Marshal.ThrowExceptionForHR(hr);
        //    }

        //    // Set file type filters.
        //    string filter = "Word Documents|*.docx|JPEG Files|*.jpg";

        //    List<COMDLG_FILTERSPEC> extensions = [];

        //    if (!string.IsNullOrEmpty(filter))
        //    {
        //        string[] tokens = filter.Split('|');
        //        if (0 == tokens.Length % 2)
        //        {
        //            // All even numbered tokens should be labels.
        //            // Odd numbered tokens are the associated extensions.
        //            for (int i = 1; i < tokens.Length; i += 2)
        //            {
        //                COMDLG_FILTERSPEC extension;

        //                extension.pszSpec = (char*)Marshal.StringToHGlobalUni(tokens[i]);
        //                extension.pszName = (char*)Marshal.StringToHGlobalUni(tokens[i - 1]);
        //                extensions.Add(extension);
        //            }
        //        }
        //    }
        //    fsd.SetFileTypes(extensions.ToArray());

        //    // Set the default folder.
        //    hr = PInvoke.SHCreateItemFromParsingName(
        //        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        //        null,
        //        typeof(IShellItem).GUID,
        //        out var directoryShellItem);
        //    if (hr < 0)
        //    {
        //        Marshal.ThrowExceptionForHR(hr);
        //    }

        //    fsd.SetFolder((IShellItem)directoryShellItem);
        //    fsd.SetDefaultFolder((IShellItem)directoryShellItem);

        //    // Set the default file name.
        //    fsd.SetFileName($"{DateTime.Now:yyyyMMddHHmm}");

        //    // Set the default extension.
        //    fsd.SetDefaultExtension(".docx");

        //    fsd.Show(new HWND(hWnd));

        //    fsd.GetResult(out var ppsi);

        //    PWSTR filename;
        //    ppsi.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, &filename);
        //}

        public async Task<string?> PickFolderAsync()
        {
            if (!Environment.IsPrivilegedProcess)
            {
                return await PickFolderWinSdkAsync();
            }
            else
            {
                return PickFolderWin32();
            }
        }

        private static async Task<string?> PickFolderWinSdkAsync()
        {
            var picker = new FolderPicker();
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }

        /// <summary>
        /// Shows a Win32 folder picker dialog using IFileOpenDialog with FOS_PICKFOLDERS.
        /// 
        /// Note: When running as an elevated (administrator) process, mapped network drives
        /// will not be visible in the dialog. This is a Windows limitation because mapped drives
        /// are per-user and are not available to elevated processes. Only drives mapped in the
        /// administrator context will appear. UNC paths (e.g., \\server\share) remain accessible.
        /// </summary>
        private static unsafe string? PickFolderWin32()
        {
            var hWnd = WindowNative.GetWindowHandle(App.MainWindow);

            HRESULT hr = PInvoke.CoCreateInstance<IFileOpenDialog>(
                typeof(FileOpenDialog).GUID,
                null,
                CLSCTX.CLSCTX_INPROC_SERVER,
                out var fod);
            hr.ThrowOnFailure();

            fod.GetOptions(out var options);
            fod.SetOptions(options | FILEOPENDIALOGOPTIONS.FOS_PICKFOLDERS | FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM);

            hr = PInvoke.SHCreateItemFromParsingName(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                null,
                typeof(IShellItem).GUID,
                out var directoryShellItem);
            if (hr.Succeeded)
            {
                fod.SetFolder((IShellItem)directoryShellItem);
                fod.SetDefaultFolder((IShellItem)directoryShellItem);
            }

            try
            {
                fod.Show(new HWND(hWnd));
            }
            catch (COMException ex) when ((uint)ex.HResult == 0x800704C7) // ERROR_CANCELLED
            {
                return null;
            }

            fod.GetResult(out var ppsi);

            if (ppsi is not null)
            {
                ppsi.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var pszPath);
                string? folderPath = pszPath.ToString();
                Marshal.FreeCoTaskMem((IntPtr)pszPath.Value);
                return folderPath;
            }

            return null;
        }
    }
}
