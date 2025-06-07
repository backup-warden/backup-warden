using System.Collections.Generic;

namespace BackupWarden.Models
{
    public class BackupConfig
    {
        public List<AppConfig> Apps { get; set; } = [];
    }
}