using System.Collections.Generic;

namespace BackupWarden.Core.Abstractions.Services.Business
{
    public interface IAppSettingsService
    {
        void SaveYamlFilePaths(IEnumerable<string> paths);
        List<string> LoadYamlFilePaths();
        void SaveBackupFolder(string path);
        string? LoadBackupFolder();
    }
}
