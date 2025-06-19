using BackupWarden.Core.Models;

namespace BackupWarden.Core.Abstractions.Services.Business
{
    public interface IYamlConfigService
    {
        BackupConfig Load(string path);
    }
}
