using System.Collections.Generic;
using System.Diagnostics;
using Windows.Storage;

namespace BackupWarden.Services
{

    public interface IAppSettingsService
    {
        void SaveYamlFilePaths(IEnumerable<string> paths);
        List<string> LoadYamlFilePaths();
        void SaveDestinationFolder(string path);
        string? LoadDestinationFolder();
    }

    public class AppSettingsService : IAppSettingsService
    {
        private const string YamlFilesKey = "YamlFilePaths";
        private const string DestinationFolderKey = "DestinationFolder";

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

        public void SaveDestinationFolder(string path)
        {
            ApplicationData.Current.LocalSettings.Values[DestinationFolderKey] = path;
        }

        public string? LoadDestinationFolder()
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(DestinationFolderKey, out var value) && value is string path)
            {
                return path;
            }
            return null;
        }
    }
}
