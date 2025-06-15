using BackupWarden.Core.Abstractions.Services.Business;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BackupWarden.Core.Services.Business
{
    public class UnpackedAppSettingsService : IAppSettingsService
    {
        private readonly string _settingsFilePath;
        private readonly AppSettings _settings;
        private readonly IFileSystemOperations _fileSystemOperations;
        private readonly ILogger<UnpackedAppSettingsService> _logger;
        private static readonly JsonSerializerOptions jsonSerializerOptions = new() { WriteIndented = true };

        private const string SettingsFileName = "appsettings.json";

        public UnpackedAppSettingsService(IFileSystemOperations fileSystemOperations, ILogger<UnpackedAppSettingsService> logger)
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
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
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
                var json = JsonSerializer.Serialize(_settings, jsonSerializerOptions);
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
            _logger.LogInformation("Saving YamlFilePaths. Count: {Count}", paths.Count());
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

        private class AppSettings
        {
            public List<string>? YamlFilePaths { get; set; } = [];
            public string? BackupFolder { get; set; }
        }
    }
}