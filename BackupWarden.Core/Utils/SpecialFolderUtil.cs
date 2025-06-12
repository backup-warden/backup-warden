using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BackupWarden.Core.Utils
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

            // Split the path into segments based on directory separators
            var segments = path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.None);

            // Resolve each segment that matches a special folder placeholder
            for (int i = 0; i < segments.Length; i++)
            {
                if (FolderResolvers.TryGetValue(segments[i], out var resolver))
                {
                    segments[i] = resolver();
                }
            }

            // Combine the resolved segments into a single valid path
            string combinedPath = Path.Combine(segments);

            // Preserve trailing slash if present in the original path
            bool originalPathEndsWithSeparator = path.EndsWith(Path.DirectorySeparatorChar.ToString()) || path.EndsWith(Path.AltDirectorySeparatorChar.ToString());
            bool combinedPathEndsWithSeparator = combinedPath.EndsWith(Path.DirectorySeparatorChar.ToString()) || combinedPath.EndsWith(Path.AltDirectorySeparatorChar.ToString());

            if (originalPathEndsWithSeparator && !combinedPathEndsWithSeparator)
            {
                return combinedPath + Path.DirectorySeparatorChar;
            }

            return combinedPath;
        }

        public static string ConvertToSpecialFolderPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                return fullPath;
            }

            // Check if the path already starts with a known special folder placeholder
            foreach (var placeholder in FolderResolvers.Keys)
            {
                if (fullPath.StartsWith(placeholder, StringComparison.OrdinalIgnoreCase))
                {
                    // If it's already a special path, return it as is,
                    // potentially normalizing the directory separators if mixed.
                    return fullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                }
            }

            // Normalize path for comparison
            string normalizedFullPath;
            try
            {
                normalizedFullPath = Path.GetFullPath(fullPath);
            }
            catch (ArgumentException)
            {
                // Path.GetFullPath can throw ArgumentException for invalid paths (e.g. "C::\foo")
                // or paths that are just placeholders but not caught above (e.g. if a new placeholder was added but not handled)
                // In such cases, or if it's a non-file system path (like a URL that GetFullPath might reject),
                // return the original path.
                return fullPath;
            }


            // Try to match the path with any of the special folders
            var matchedEntries = FolderResolvers
                .Select(kvp =>
                {
                    var specialPath = kvp.Value();
                    // Ensure specialPath is not null or empty before further processing
                    if (string.IsNullOrEmpty(specialPath))
                    {
                        return null;
                    }
                    // Normalize specialPath as well to ensure consistent comparison
                    var normalizedSpecialPath = Path.GetFullPath(specialPath);
                    return new
                    {
                        Placeholder = kvp.Key,
                        Path = normalizedSpecialPath,
                        PathLength = normalizedSpecialPath.Length,
                        SeparatorCount = normalizedSpecialPath.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar)
                    };
                })
                .Where(entry => entry != null && !string.IsNullOrEmpty(entry.Path) && normalizedFullPath.StartsWith(entry.Path, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry!.PathLength)
                .ThenByDescending(entry => entry!.SeparatorCount)
                .ToList();

            if (matchedEntries?.Count > 0) // Updated to handle nullable matchedEntries
            {
                var bestMatch = matchedEntries.First();
                var relativePath = normalizedFullPath[bestMatch!.PathLength..];

                // Ensure we have a path separator at the beginning of the relative path if needed
                if (relativePath.Length > 0 && relativePath[0] != Path.DirectorySeparatorChar && relativePath[0] != Path.AltDirectorySeparatorChar)
                {
                    relativePath = Path.DirectorySeparatorChar + relativePath;
                }
                // Normalize to use Path.DirectorySeparatorChar for the appended part
                else if (relativePath.Length > 0 && (relativePath[0] == Path.AltDirectorySeparatorChar || relativePath[0] == Path.DirectorySeparatorChar))
                {
                    // Ensure it starts with the standard separator and remove any leading duplicate if it was already there
                    relativePath = Path.DirectorySeparatorChar + relativePath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }


                return bestMatch.Placeholder + relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            }

            return normalizedFullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }
    }
}
