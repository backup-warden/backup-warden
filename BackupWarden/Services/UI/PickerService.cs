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
using Windows.Win32.UI.Shell.Common;

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
            if (!Environment.IsPrivilegedProcess)
            {
                return await PickFilesWinSdkAsync(fileTypeFilters, allowMultiple);
            }
            else
            {
                return PickFilesWin32(fileTypeFilters, allowMultiple);
            }
        }

        private static async Task<List<string>> PickFilesWinSdkAsync(IEnumerable<string> fileTypeFilters, bool allowMultiple)
        {
            var picker = new FileOpenPicker();
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);
            foreach (var filter in fileTypeFilters)
            {
                picker.FileTypeFilter.Add(filter);
            }

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

        private static unsafe List<string> PickFilesWin32(IEnumerable<string> fileTypeFilters, bool allowMultiple)
        {
            var hWnd = WindowNative.GetWindowHandle(App.MainWindow);

            HRESULT hr = PInvoke.CoCreateInstance<IFileOpenDialog>(
                typeof(FileOpenDialog).GUID,
                null,
                CLSCTX.CLSCTX_INPROC_SERVER,
                out var fod);
            hr.ThrowOnFailure();

            IShellItem* directoryShellItem = null;
            IShellItemArray* resultsArray = null;
            var allocatedPtrs = new List<IntPtr>();
            try
            {
                fod->GetOptions(out var options);
                options |= FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM;
                if (allowMultiple)
                {
                    options |= FILEOPENDIALOGOPTIONS.FOS_ALLOWMULTISELECT;
                }
                fod->SetOptions(options);

                var filters = fileTypeFilters?.ToArray() ?? [];
                var specs = new List<COMDLG_FILTERSPEC>();

                // Individual filters
                foreach (var filter in filters)
                {
                    var pattern = filter.StartsWith('.') ? $"*{filter}" : filter;
                    var name = pattern;
                    var namePtr = Marshal.StringToHGlobalUni(name);
                    var patternPtr = Marshal.StringToHGlobalUni(pattern);
                    allocatedPtrs.Add(namePtr);
                    allocatedPtrs.Add(patternPtr);
                    specs.Add(new COMDLG_FILTERSPEC
                    {
                        pszName = (char*)namePtr,
                        pszSpec = (char*)patternPtr
                    });
                }

                // "All files" filter
                if (filters.Length > 1)
                {
                    var patterns = filters.Select(f => f.StartsWith('.') ? $"*{f}" : f).ToArray();
                    string displayName = "All files";
                    string patternString = string.Join(";", patterns);
                    var allNamePtr = Marshal.StringToHGlobalUni($"{displayName} ({string.Join(", ", patterns)})");
                    var allSpecPtr = Marshal.StringToHGlobalUni(patternString);
                    allocatedPtrs.Add(allNamePtr);
                    allocatedPtrs.Add(allSpecPtr);
                    specs.Add(new COMDLG_FILTERSPEC
                    {
                        pszName = (char*)allNamePtr,
                        pszSpec = (char*)allSpecPtr
                    });
                }

                // Set filters
                if (specs.Count > 0)
                {
                    fixed (COMDLG_FILTERSPEC* pSpecs = specs.ToArray())
                    {
                        fod->SetFileTypes((uint)specs.Count, pSpecs);
                        // Select the last filter ("All files") by default
                        fod->SetFileTypeIndex((uint)specs.Count);
                    }
                }

                // Set default folder
                hr = PInvoke.SHCreateItemFromParsingName(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    null,
                    typeof(IShellItem).GUID,
                    out var dirShellItem);
                if (hr.Succeeded)
                {
                    directoryShellItem = (IShellItem*)dirShellItem;
                    fod->SetFolder(directoryShellItem);
                    fod->SetDefaultFolder(directoryShellItem);
                }

                try
                {
                    fod->Show(new HWND(hWnd));
                }
                catch (COMException ex) when ((uint)ex.HResult == 0x800704C7)
                {
                    return [];
                }

                if (allowMultiple)
                {
                    fod->GetResults(&resultsArray);
                    if (resultsArray is null)
                    {
                        return [];
                    }

                    resultsArray->GetCount(out uint count);
                    var result = new List<string>((int)count);
                    for (uint i = 0; i < count; i++)
                    {
                        IShellItem* item = null;
                        resultsArray->GetItemAt(i, &item);
                        if (item is not null)
                        {
                            item->GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var pszPath);
                            result.Add(pszPath.ToString());
                            Marshal.FreeCoTaskMem((IntPtr)pszPath.Value);
                            item->Release();
                        }
                    }
                    return result;
                }
                else
                {
                    IShellItem* ppsi = null;
                    fod->GetResult(&ppsi);
                    if (ppsi is not null)
                    {
                        ppsi->GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var pszPath);
                        string? filePath = pszPath.ToString();
                        Marshal.FreeCoTaskMem((IntPtr)pszPath.Value);
                        ppsi->Release();
                        return filePath is not null ? [filePath] : [];
                    }
                    return [];
                }
            }
            finally
            {
                // Free all allocated filter strings
                foreach (var ptr in allocatedPtrs)
                    Marshal.FreeHGlobal(ptr);

                if (resultsArray is not null) resultsArray->Release();
                if (directoryShellItem is not null) directoryShellItem->Release();
                if (fod is not null) fod->Release();
            }
        }

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

            void* directoryShellItem = null;
            IShellItem* ppsi = null;
            try
            {
                fod->GetOptions(out var options);
                fod->SetOptions(options | FILEOPENDIALOGOPTIONS.FOS_PICKFOLDERS | FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM);

                hr = PInvoke.SHCreateItemFromParsingName(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    null,
                    typeof(IShellItem).GUID,
                    out directoryShellItem);
                if (hr.Succeeded)
                {
                    fod->SetFolder((IShellItem*)directoryShellItem);
                    fod->SetDefaultFolder((IShellItem*)directoryShellItem);
                }

                try
                {
                    fod->Show(new HWND(hWnd));
                }
                catch (COMException ex) when ((uint)ex.HResult == 0x800704C7)
                {
                    return null;
                }

                fod->GetResult(&ppsi);

                if (ppsi is not null)
                {
                    ppsi->GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var pszPath);
                    string? folderPath = pszPath.ToString();
                    Marshal.FreeCoTaskMem((IntPtr)pszPath.Value);
                    return folderPath;
                }

                return null;
            }
            finally
            {
                if (ppsi is not null) ppsi->Release();
                if (directoryShellItem is not null) ((IShellItem*)directoryShellItem)->Release();
                if (fod is not null) fod->Release();
            }
        }
    }
}
