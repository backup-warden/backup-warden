using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupWarden.Models
{
    public class BackupConfig
    {
        public List<AppConfig> Apps { get; set; } = [];
    }

    public class AppConfig
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<string> Paths { get; set; } = [];
        public string? Key { get; set; }
        public string? Account { get; set; }
        public string? Mods { get; set; }
    }
}
