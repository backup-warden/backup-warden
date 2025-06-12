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
