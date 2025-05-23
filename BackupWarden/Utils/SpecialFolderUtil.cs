using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BackupWarden.Utils
{
    public static partial class SpecialFolderUtil
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

        /// <summary>
        /// Maps a backup path containing a user profile segment (e.g., C/Users/olduser/...)
        /// to the current user's profile path.
        /// </summary>
        public static string MapBackupUserPathToCurrentUser(string backupPath)
        {
            if (string.IsNullOrEmpty(backupPath))
            {
                return backupPath;
            }

            // Normalize separators for matching
            var normalized = backupPath.Replace('\\', '/');

            // Regex to match drive letter and user profile (e.g., C/Users/username/)
            var match = MatchDriverLetterUserProfileRegex().Match(normalized);
            if (match.Success)
            {
                // Get current user's profile path (e.g., C:\Users\currentuser)
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var rest = match.Groups["rest"].Value.Replace('/', Path.DirectorySeparatorChar);
                return Path.Combine(userProfile, rest);
            }

            return backupPath.Replace('/', Path.DirectorySeparatorChar);
        }

        [GeneratedRegex(@"^(?<drive>[A-Za-z])/(Users)/[^/]+/(?<rest>.*)$")]
        private static partial Regex MatchDriverLetterUserProfileRegex();
    }
}
