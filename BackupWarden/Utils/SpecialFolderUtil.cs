using System;
using System.Collections.Generic;
using System.Linq;

namespace BackupWarden.Utils
{
    public static class SpecialFolderUtil
    {
        private static readonly Dictionary<string, Func<string>> FolderResolvers = new(StringComparer.OrdinalIgnoreCase)
    {
        { "%LocalAppData%", () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) },
        { "%AppData%", () => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) },
        { "%UserProfile%", () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) },
        { "%Documents%", () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) },
        { "%Desktop%", () => Environment.GetFolderPath(Environment.SpecialFolder.Desktop) },
        { "%ProgramFiles%", () => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) },
        { "%ProgramFiles(x86)%", () => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) },
        { "%ProgramData%", () => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) },
        { "%SystemRoot%", () => Environment.GetFolderPath(Environment.SpecialFolder.Windows) },
        { "%SystemDrive%", () => Environment.GetFolderPath(Environment.SpecialFolder.System) },
    };

        public static string ExpandSpecialFolders(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            foreach (var kvp in FolderResolvers.Where(w => path.Contains(w.Key, StringComparison.OrdinalIgnoreCase)))
            {
                path = path.Replace(kvp.Key, kvp.Value(), StringComparison.OrdinalIgnoreCase);
            }
            return path;
        }
    }
}
