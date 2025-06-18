using BackupWarden.Core.Abstractions.Services.Business;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackupWarden.Core.Services.Business
{
    public class UnpackagedAppSettingsService : IAppSettingsService
    {
        private readonly string _settingsFilePath;
        private readonly AppSettings _settings;
        private readonly IFileSystemOperations _fileSystemOperations;
        private readonly ILogger<UnpackagedAppSettingsService> _logger;

        private const string SettingsFileName = "appsettings.json";

        public UnpackagedAppSettingsService(IFileSystemOperations fileSystemOperations, ILogger<UnpackagedAppSettingsService> logger)
        {
            _fileSystemOperations = fileSystemOperations;
            _logger = logger;

            // Store settings in LocalApplicationData for unpacked apps
            var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDataFolder = Path.Combine(localAppDataPath, "BackupWarden");
            _fileSystemOperations.CreateDirectory(appDataFolder); // Ensure the directory exists
            _settingsFilePath = Path.Combine(appDataFolder, SettingsFileName);

            _settings = LoadSettingsFromFile();
        }

        private AppSettings LoadSettingsFromFile()
        {
            try
            {
                if (_fileSystemOperations.FileExists(_settingsFilePath))
                {
                    var json = _fileSystemOperations.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
                    if (settings != null)
                    {
                        _logger.LogInformation("Successfully loaded settings from {FilePath}", _settingsFilePath);
                        return settings;
                    }
                    _logger.LogWarning("Failed to deserialize settings from {FilePath}, or file was empty. Returning default settings.", _settingsFilePath);
                }
                else
                {
                    _logger.LogInformation("Settings file not found at {FilePath}. Returning default settings.", _settingsFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading settings from {FilePath}. Returning default settings.", _settingsFilePath);
            }
            return new AppSettings();
        }

        private void SaveSettingsToFile()
        {
            try
            {
                var json = JsonSerializer.Serialize(_settings, AppSettingsJsonContext.Default.AppSettings);
                _fileSystemOperations.WriteAllText(_settingsFilePath, json);
                _logger.LogInformation("Successfully saved settings to {FilePath}", _settingsFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving settings to {FilePath}", _settingsFilePath);
            }
        }

        public List<string> LoadYamlFilePaths()
        {
            _logger.LogDebug("Loading YamlFilePaths. Count: {Count}", _settings.YamlFilePaths?.Count ?? 0);
            return _settings.YamlFilePaths ?? [];
        }

        public void SaveYamlFilePaths(IEnumerable<string> paths)
        {
            var count = paths.Count();
            _logger.LogInformation("Saving YamlFilePaths. Count: {Count}", count);
            _settings.YamlFilePaths = [.. paths];
            SaveSettingsToFile();
        }

        public string? LoadBackupFolder()
        {
            _logger.LogDebug("Loading BackupFolder. Path: {Path}", _settings.BackupFolder ?? "Not set");
            return _settings.BackupFolder;
        }

        public void SaveBackupFolder(string path)
        {
            _logger.LogInformation("Saving BackupFolder. Path: {Path}", path);
            _settings.BackupFolder = path;
            SaveSettingsToFile();
        }

        public class AppSettings
        {
            public List<string>? YamlFilePaths { get; set; } = [];
            public string? BackupFolder { get; set; }
        }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(UnpackagedAppSettingsService.AppSettings))]
    internal partial class AppSettingsJsonContext : JsonSerializerContext
    {
    }


}