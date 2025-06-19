using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace BackupWarden.Core.Models
{
    public class BackupConfig
    {
        public List<AppConfig> Apps { get; set; } = [];
    }
}