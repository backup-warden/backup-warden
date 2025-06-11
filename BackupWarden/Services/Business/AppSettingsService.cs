using BackupWarden.Core.Abstractions.Services.Business;
using System.Collections.Generic;
using Windows.Storage;

namespace BackupWarden.Services.Business
{
    public class AppSettingsService : IAppSettingsService
    {
        private const string YamlFilesKey = "YamlFilePaths";
        private const string BackupFolderKey = "BackupFolder";

        public void SaveYamlFilePaths(IEnumerable<string> paths)
        {
            var joined = string.Join("|", paths);
            ApplicationData.Current.LocalSettings.Values[YamlFilesKey] = joined;
        }

        public List<string> LoadYamlFilePaths()
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(YamlFilesKey, out var value) && value is string joined)
            {
                return [.. joined.Split('|', System.StringSplitOptions.RemoveEmptyEntries)];
            }
            return [];
        }

        public void SaveBackupFolder(string path)
        {
            ApplicationData.Current.LocalSettings.Values[BackupFolderKey] = path;
        }

        public string? LoadBackupFolder()
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(BackupFolderKey, out var value) && value is string path)
            {
                return path;
            }
            return null;
        }
    }
}
