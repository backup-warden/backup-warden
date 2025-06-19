using BackupWarden.Core.Abstractions.Services.Business;
using BackupWarden.Core.Models;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BackupWarden.Core.Services.Business
{
    public class YamlConfigService : IYamlConfigService
    {
        private readonly IDeserializer _deserializer;

        public YamlConfigService()
        {
            _deserializer = new StaticDeserializerBuilder(new BackupConfigYamlContext())
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
        }

        public BackupConfig Load(string path)
        {
            using var reader = new StreamReader(path);
            return _deserializer.Deserialize<BackupConfig>(reader);
        }
    }

    [YamlSerializable(typeof(BackupConfig))]
    [YamlSerializable(typeof(AppConfig))]
    [YamlStaticContext]
    public partial class BackupConfigYamlContext : StaticContext
    {
    }
}
