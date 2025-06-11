using BackupWarden.Core.Abstractions.Services.Business;
using BackupWarden.Core.Models;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BackupWarden.Core.Services.Business
{
    public class YamlConfigService : IYamlConfigService
    {
        public BackupConfig LoadConfig(string path)
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            using var reader = new StreamReader(path);
            return deserializer.Deserialize<BackupConfig>(reader);
        }
    }
}
