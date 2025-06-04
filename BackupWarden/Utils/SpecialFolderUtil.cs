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

        public static string ConvertToSpecialFolderPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                return fullPath;
            }

            // Normalize path for comparison
            fullPath = Path.GetFullPath(fullPath);

            // Try to match the path with any of the special folders
            var matchedEntries = FolderResolvers
                .Select(kvp =>
                {
                    var specialPath = kvp.Value();
                    return new
                    {
                        Placeholder = kvp.Key,
                        Path = specialPath,
                        PathLength = specialPath.Length,
                        SeparatorCount = specialPath.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar)
                    };
                })
                .Where(entry => !string.IsNullOrEmpty(entry.Path) && fullPath.StartsWith(entry.Path, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry.PathLength) // Primary sort: longest path first
                .ThenByDescending(entry => entry.SeparatorCount) // Secondary sort: most separators first
                .ToList();

            if (matchedEntries.Count > 0)
            {
                var bestMatch = matchedEntries.First();
                var relativePath = fullPath[bestMatch.PathLength..];

                // Ensure we have a path separator at the beginning of the relative path if needed
                if (relativePath.Length > 0 && relativePath[0] != Path.DirectorySeparatorChar && relativePath[0] != Path.AltDirectorySeparatorChar)
                {
                    relativePath = Path.DirectorySeparatorChar + relativePath;
                }
                // Normalize to use Path.DirectorySeparatorChar for the appended part
                else if (relativePath.Length > 0 && relativePath[0] == Path.AltDirectorySeparatorChar)
                {
                    relativePath = Path.DirectorySeparatorChar + relativePath[1..];
                }


                return bestMatch.Placeholder + relativePath;
            }

            return fullPath;
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
