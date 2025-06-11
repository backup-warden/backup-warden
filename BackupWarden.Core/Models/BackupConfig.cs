using System.Collections.Generic;

namespace BackupWarden.Core.Models
{
    public class BackupConfig
    {
        public List<AppConfig> Apps { get; set; } = [];
    }
}