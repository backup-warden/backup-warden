using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using BackupWarden.Core.Utils;
using System.IO;

namespace BackupWarden.Tests.Utils
{
    public class SpecialFolderUtilTests
    {
        [Theory]
        [InlineData("%LocalAppData%\\MyApp", "LocalApplicationData")]
        [InlineData("%AppData%\\MyApp", "ApplicationData")]
        [InlineData("%UserProfile%\\Documents", "UserProfile")]
        [InlineData("%Documents%\\MyGame", "MyDocuments")]
        [InlineData("%Desktop%\\MyFile.txt", "Desktop")]
        [InlineData("%ProgramFiles%\\MyTool", "ProgramFiles")]
        [InlineData("%ProgramFiles(x86)%\\OldTool", "ProgramFilesX86")]
        [InlineData("%ProgramData%\\SharedData", "CommonApplicationData")]
        [InlineData("%SystemRoot%\\System32", "Windows")]
        [InlineData("%SystemDrive%\\MySystemFile.dll", "System")]
        public void ExpandSpecialFolders_ShouldResolveKnownFolder(string inputPath, string specialFolderEnumName)
        {
            var specialFolder = (Environment.SpecialFolder)Enum.Parse(typeof(Environment.SpecialFolder), specialFolderEnumName);
            string expectedPath = Path.Combine(Environment.GetFolderPath(specialFolder), inputPath.Split('\\').Last());
            
            string actualPath = SpecialFolderUtil.ExpandSpecialFolders(inputPath);

            Assert.Equal(expectedPath, actualPath, ignoreCase: true);
        }

        [Fact]
        public void ExpandSpecialFolders_ShouldResolveMultipleFolders()
        {
            string inputPath = "%LocalAppData%\\MyApp\\%UserProfile%\\MyFile.txt";
            string expectedPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyApp",
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "MyFile.txt"
            );

            string actualPath = SpecialFolderUtil.ExpandSpecialFolders(inputPath);

            Assert.Equal(expectedPath, actualPath, ignoreCase: true);
        }

        [Fact]
        public void ExpandSpecialFolders_NoSpecialFolders_ReturnsSamePath()
        {
            string inputPath = "C:\\RegularPath\\MyApp";
            
            string actualPath = SpecialFolderUtil.ExpandSpecialFolders(inputPath);
            
            Assert.Equal(inputPath, actualPath);
        }

        [Theory]
        [InlineData("")]
        public void ExpandSpecialFolders_NullOrEmptyPath_ReturnsSamePath(string inputPath)
        {
            string actualPath = SpecialFolderUtil.ExpandSpecialFolders(inputPath);
            Assert.Equal(inputPath, actualPath);
        }

        [Fact]
        public void ExpandSpecialFolders_UnknownPlaceholder_ReturnsSamePath()
        {
            string inputPath = "%UnknownPlaceholder%\\MyApp";
            
            string actualPath = SpecialFolderUtil.ExpandSpecialFolders(inputPath);

            Assert.Equal(inputPath, actualPath);
        }

        [Fact]
        public void ExpandSpecialFolders_PathIsOnlySpecialFolder_ResolvesCorrectly()
        {
            string inputPath = "%ProgramFiles%";
            string expectedPath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string actualPath = SpecialFolderUtil.ExpandSpecialFolders(inputPath);
            Assert.Equal(expectedPath, actualPath, ignoreCase: true);

            inputPath = "%AppData%";
            expectedPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            actualPath = SpecialFolderUtil.ExpandSpecialFolders(inputPath);
            Assert.Equal(expectedPath, actualPath, ignoreCase: true);
        }

        [Fact]
        public void ExpandSpecialFolders_PathWithMixedSeparators_ResolvesCorrectly()
        {
            string inputPath = $"%AppData%/MyCoolApp\\UserSettings";
            string expectedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyCoolApp", "UserSettings");
            string actualPath = SpecialFolderUtil.ExpandSpecialFolders(inputPath);
            Assert.Equal(expectedPath, actualPath, ignoreCase: true);
        }

        [Theory]
        [InlineData("%Desktop%\\MyFile.txt", "Desktop", "MyFile.txt", false)] // Standard
        [InlineData("%Documents%\\MyFolder\\", "MyDocuments", "MyFolder", true)] // Trailing
        [InlineData("%ProgramFiles%/AnotherApp/", "ProgramFiles", "AnotherApp", true)] // Trailing with alt separator
        public void ExpandSpecialFolders_PathVariations_ResolvesCorrectly(string inputPath, string specialFolderEnumName, string subPath, bool endsWithSeparator)
        {
            var specialFolder = (Environment.SpecialFolder)Enum.Parse(typeof(Environment.SpecialFolder), specialFolderEnumName);
            string resolvedCorePath = Path.Combine(Environment.GetFolderPath(specialFolder), subPath);
            string expectedPath = endsWithSeparator ? resolvedCorePath + Path.DirectorySeparatorChar : resolvedCorePath;
            // Path.Combine might add a trailing slash if the last segment is empty and the path is a directory.
            // For "folder\", Path.Combine(resolved, "folder", "") might result in "resolved\folder\"
            // Let's ensure the expected path matches this behavior if input has trailing slash.

            string actualPath = SpecialFolderUtil.ExpandSpecialFolders(inputPath);

            // Normalize paths for comparison to handle potential differences in trailing slashes if not explicitly managed by endsWithSeparator logic
            var normalizedActualPath = Path.GetFullPath(actualPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var normalizedExpectedPath = Path.GetFullPath(expectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            Assert.Equal(normalizedExpectedPath, normalizedActualPath, ignoreCase: true);
            if (endsWithSeparator)
            {
                Assert.True(actualPath.EndsWith(Path.DirectorySeparatorChar.ToString()) || actualPath.EndsWith(Path.AltDirectorySeparatorChar.ToString()));
            }
            else
            {
                Assert.False(actualPath.EndsWith(Path.DirectorySeparatorChar.ToString()) || actualPath.EndsWith(Path.AltDirectorySeparatorChar.ToString()));
            }
        }

        [Theory]
        [InlineData("LocalApplicationData", "%LocalAppData%")]
        [InlineData("ApplicationData", "%AppData%")]
        [InlineData("UserProfile", "%UserProfile%")]
        [InlineData("MyDocuments", "%Documents%")]
        [InlineData("Desktop", "%Desktop%")]
        [InlineData("ProgramFiles", "%ProgramFiles%")]
        [InlineData("ProgramFilesX86", "%ProgramFiles(x86)%")]
        [InlineData("CommonApplicationData", "%ProgramData%")]
        [InlineData("Windows", "%SystemRoot%")]
        [InlineData("System", "%SystemDrive%")]
        public void ConvertToSpecialFolderPath_ShouldConvertKnownFolder(string specialFolderEnumName, string expectedPlaceholder)
        {
            var specialFolder = (Environment.SpecialFolder)Enum.Parse(typeof(Environment.SpecialFolder), specialFolderEnumName);
            string inputPath = Path.Combine(Environment.GetFolderPath(specialFolder), "MyApp", "MyFile.txt");
            string expectedPath = Path.Combine(expectedPlaceholder, "MyApp", "MyFile.txt");
            
            string actualPath = SpecialFolderUtil.ConvertToSpecialFolderPath(inputPath);

            Assert.Equal(expectedPath, actualPath, ignoreCase: true);
        }
        
        [Theory]
        [InlineData("LocalApplicationData", "%LocalAppData%")]
        [InlineData("ApplicationData", "%AppData%")]
        [InlineData("UserProfile", "%UserProfile%")]
        [InlineData("MyDocuments", "%Documents%")]
        [InlineData("Desktop", "%Desktop%")]
        [InlineData("ProgramFiles", "%ProgramFiles%")]
        [InlineData("ProgramFilesX86", "%ProgramFiles(x86)%")]
        [InlineData("CommonApplicationData", "%ProgramData%")]
        [InlineData("Windows", "%SystemRoot%")]
        [InlineData("System", "%SystemDrive%")]
        public void ConvertToSpecialFolderPath_ExactMatch_ReturnsPlaceholderOnly(string specialFolderEnumName, string expectedPlaceholder)
        {
            var specialFolder = (Environment.SpecialFolder)Enum.Parse(typeof(Environment.SpecialFolder), specialFolderEnumName);
            string inputPath = Environment.GetFolderPath(specialFolder);
            // Ensure inputPath is not null or empty, which can happen for some special folders on some systems/environments
            if (string.IsNullOrEmpty(inputPath))
            {
                // Skip test for this environment if path is not available (e.g. ProgramFilesX86 on a 32-bit system)
                return; 
            }
            string actualPath = SpecialFolderUtil.ConvertToSpecialFolderPath(inputPath);
            Assert.Equal(expectedPlaceholder, actualPath, ignoreCase: true);
        }

        [Fact]
        public void ConvertToSpecialFolderPath_LongestMatch_ShouldBePreferred()
        {
            string systemFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.System); // e.g. C:\WINDOWS\system32

            string inputPathWithinSystem32 = Path.Combine(systemFolderPath, "drivers", "etc", "hosts");
            string expectedPathForSystem32 = Path.Combine("%SystemDrive%", "drivers", "etc", "hosts");
            string actualPathForSystem32 = SpecialFolderUtil.ConvertToSpecialFolderPath(inputPathWithinSystem32);
            Assert.Equal(expectedPathForSystem32, actualPathForSystem32, ignoreCase: true);

            string userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); // C:\Users\YourUser
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);    // C:\Users\YourUser\Documents

            if (documentsPath.StartsWith(userProfilePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                string inputPathInDocuments = Path.Combine(documentsPath, "MyWork", "ProjectA");
                string expectedPathForDocuments = Path.Combine("%Documents%", "MyWork", "ProjectA");
                string actualPathForDocuments = SpecialFolderUtil.ConvertToSpecialFolderPath(inputPathInDocuments);
                Assert.Equal(expectedPathForDocuments, actualPathForDocuments, ignoreCase: true);
            }
        }

        [Fact]
        public void ConvertToSpecialFolderPath_NoMatchingSpecialFolder_ReturnsSamePath()
        {
            string inputPath = "D:\\SomeOtherDrive\\MyData\\file.dat";
             // If D: is not a system drive with a corresponding special folder like %SystemDrive%
            string actualPath = SpecialFolderUtil.ConvertToSpecialFolderPath(inputPath);
            Assert.Equal(inputPath, actualPath, ignoreCase: true);

            inputPath = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "NonSpecialFolder", "data.txt");
            actualPath = SpecialFolderUtil.ConvertToSpecialFolderPath(inputPath);
            Assert.Equal(inputPath, actualPath, ignoreCase: true);
        }

        [Theory]
        [InlineData("")]
        public void ConvertToSpecialFolderPath_NullOrEmptyPath_ReturnsSamePath(string inputPath)
        {
            string actualPath = SpecialFolderUtil.ConvertToSpecialFolderPath(inputPath);
            Assert.Equal(inputPath, actualPath);
        }

        [Fact]
        public void ConvertToSpecialFolderPath_AlreadySpecialPlaceholder_ReturnsSamePath()
        {
            string inputPath = "%LocalAppData%\\SomeApp";
            string actualPath = SpecialFolderUtil.ConvertToSpecialFolderPath(inputPath);
            Assert.Equal(inputPath, actualPath, ignoreCase: true);
        }

        [Theory]
        [InlineData("\\\\Server\\Share\\Folder\\file.txt")]
        [InlineData("//AnotherServer/AnotherShare/doc.pdf")]
        public void ConvertToSpecialFolderPath_NetworkPath_ReturnsSamePath(string networkPath)
        {
            string actualPath = SpecialFolderUtil.ConvertToSpecialFolderPath(networkPath);
            Assert.Equal(Path.GetFullPath(networkPath), actualPath, ignoreCase: true);
        }

        [Fact]
        public void ConvertToSpecialFolderPath_PathOnDifferentDrive_NoSpecialFolderMatch_ReturnsSamePath()
        {
            // Assuming tests run on C: and D: is not a special folder root
            string inputPath = "D:\\MyCustomData\\backup.zip";
            string actualPath = SpecialFolderUtil.ConvertToSpecialFolderPath(inputPath);
            Assert.Equal(inputPath, actualPath, ignoreCase: true);
        }
    }
}
