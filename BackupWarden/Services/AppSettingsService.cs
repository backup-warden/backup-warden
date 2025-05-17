using System.Collections.Generic;
using System.Diagnostics;
using Windows.Storage;

namespace BackupWarden.Services
{

    public interface IAppSettingsService
    {
        void SaveYamlFilePaths(IEnumerable<string> paths);
        List<string> LoadYamlFilePaths();
    }

    public class AppSettingsService : IAppSettingsService
    {
        private const string YamlFilesKey = "YamlFilePaths";

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
    }
}
