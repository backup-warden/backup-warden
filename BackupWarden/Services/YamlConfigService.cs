using BackupWarden.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BackupWarden.Services
{
    public interface IYamlConfigService
    {
        BackupConfig LoadConfig(string path);
    }

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
