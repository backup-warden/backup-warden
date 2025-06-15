using BackupWarden.Core.Abstractions.Services.Business;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using Windows.Storage;

namespace BackupWarden.Services.Business
{
    public class MsixAppSettingsService : IAppSettingsService
    {
        private const string YamlFilesKey = "YamlFilePaths";
        private const string BackupFolderKey = "BackupFolder";
        private readonly ILogger<MsixAppSettingsService> _logger;

        public MsixAppSettingsService(ILogger<MsixAppSettingsService> logger)
        {
            _logger = logger;
        }

        public void SaveYamlFilePaths(IEnumerable<string> paths)
        {
            var joined = string.Join("|", paths);
            ApplicationData.Current.LocalSettings.Values[YamlFilesKey] = joined;
            _logger.LogInformation("Saved YamlFilePaths to MSIX local settings. Count: {Count}", paths?.Count() ?? 0);
        }

        public List<string> LoadYamlFilePaths()
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(YamlFilesKey, out var value) && value is string joined)
            {
                List<string> paths = [.. joined.Split('|', System.StringSplitOptions.RemoveEmptyEntries)];
                _logger.LogInformation("Loaded YamlFilePaths from MSIX local settings. Count: {Count}", paths.Count);
                return paths;
            }
            _logger.LogInformation("No YamlFilePaths found in MSIX local settings.");
            return [];
        }

        public void SaveBackupFolder(string path)
        {
            ApplicationData.Current.LocalSettings.Values[BackupFolderKey] = path;
            _logger.LogInformation("Saved BackupFolder to MSIX local settings. Path: {Path}", path);
        }

        public string? LoadBackupFolder()
        {
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue(BackupFolderKey, out var value) && value is string path)
            {
                _logger.LogInformation("Loaded BackupFolder from MSIX local settings. Path: {Path}", path);
                return path;
            }
            _logger.LogInformation("No BackupFolder found in MSIX local settings.");
            return null;
        }
    }
}