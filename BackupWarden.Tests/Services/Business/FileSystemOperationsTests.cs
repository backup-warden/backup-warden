using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using BackupWarden.Core.Services.Business;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;


namespace BackupWarden.Tests.Services.Business
{
    public class TestFileSystemStream : FileSystemStream
    {
        public TestFileSystemStream(Stream stream, string path, bool isAsync)
            : base(stream, path, isAsync)
        {
        }
    }

    public class FileSystemOperationsTests
    {
        private readonly Mock<ILogger<FileSystemOperations>> _mockLogger;
        private readonly MockFileSystem _mockFileSystem;
        private readonly FileSystemOperations _fileSystemOperations;

        public FileSystemOperationsTests()
        {
            _mockLogger = new Mock<ILogger<FileSystemOperations>>();
            _mockFileSystem = new MockFileSystem();
            _fileSystemOperations = new FileSystemOperations(_mockLogger.Object, _mockFileSystem);
        }

        [Fact]
        public void CopyFile_ShouldCopyFile_WhenSourceExists()
        {
            // Arrange
            string sourceFile = "C:\\source\\test.txt";
            string destFile = "C:\\dest\\test.txt";
            _mockFileSystem.AddFile(sourceFile, new MockFileData("test content"));
            _mockFileSystem.AddDirectory("C:\\dest");

            // Act
            _fileSystemOperations.CopyFile(sourceFile, destFile);

            // Assert
            Assert.True(_mockFileSystem.FileExists(destFile));
            Assert.Equal("test content", _mockFileSystem.File.ReadAllText(destFile));
        }

        [Fact]
        public void CopyFile_ShouldCreateDestinationDirectory_IfNotExists()
        {
            // Arrange
            string sourceFile = "C:\\source\\test.txt";
            string destFile = "C:\\new_dest\\subdir\\test.txt";
            _mockFileSystem.AddFile(sourceFile, new MockFileData("test content"));

            // Act
            _fileSystemOperations.CopyFile(sourceFile, destFile);

            // Assert
            Assert.True(_mockFileSystem.Directory.Exists("C:\\new_dest\\subdir"));
            Assert.True(_mockFileSystem.FileExists(destFile));
        }

        [Fact]
        public void CopyFile_ShouldRetry_OnIOException()
        {
            // Arrange
            string sourceFile = "C:\\source\\retry.txt";
            string destFile = "C:\\dest\\retry.txt";
            _mockFileSystem.AddDirectory("C:\\dest"); // Ensure dest directory exists

            var mockFile = new Mock<IFile>();
            var mockPath = new Mock<IPath>();
            var mockDirectory = new Mock<IDirectory>();
            var mockFileStreamFactory = new Mock<IFileStreamFactory>();

            var attempt = 0;
            mockFileStreamFactory.Setup(fs => fs.New(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, It.IsAny<int>(), false))
                .Returns(() =>
                {
                    if (attempt < 2)
                    {
                        attempt++;
                        throw new IOException("Simulated IO error on read");
                    }
                    // Use the concrete TestFileSystemStream
                    return new TestFileSystemStream(new MemoryStream(), sourceFile, false); 
                });

            mockFileStreamFactory.Setup(fs => fs.New(destFile, FileMode.Create, FileAccess.Write, FileShare.None, It.IsAny<int>(), false))
                // Use the concrete TestFileSystemStream
                .Returns(new TestFileSystemStream(new MemoryStream(), destFile, false)); 

            mockPath.Setup(p => p.GetDirectoryName(destFile)).Returns("C:\\dest");
            mockDirectory.Setup(d => d.Exists("C:\\dest")).Returns(true);

            _mockFileSystem.AddFile(sourceFile, new MockFileData("retry content"));

            var customMockFileSystem = new Mock<IFileSystem>();
            customMockFileSystem.Setup(fs => fs.File).Returns(mockFile.Object);
            customMockFileSystem.Setup(fs => fs.Path).Returns(mockPath.Object);
            customMockFileSystem.Setup(fs => fs.Directory).Returns(mockDirectory.Object);
            customMockFileSystem.Setup(fs => fs.FileStream).Returns(mockFileStreamFactory.Object);

            var fileSystemOpsWithCustomMock = new FileSystemOperations(_mockLogger.Object, customMockFileSystem.Object);

            // Act
            fileSystemOpsWithCustomMock.CopyFile(sourceFile, destFile);

            // Assert
            mockFileStreamFactory.Verify(fs => fs.New(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, It.IsAny<int>(), false), Times.Exactly(3));
        }


        [Fact]
        public void DeleteFile_ShouldDeleteFile_WhenFileExists()
        {
            // Arrange
            string filePath = "C:\\test_to_delete.txt";
            _mockFileSystem.AddFile(filePath, new MockFileData("delete me"));

            // Act
            _fileSystemOperations.DeleteFile(filePath);

            // Assert
            Assert.False(_mockFileSystem.FileExists(filePath));
        }

        [Fact]
        public void CreateDirectory_ShouldCreateDirectory()
        {
            // Arrange
            string dirPath = "C:\\new_directory\\subdir";

            // Act
            _fileSystemOperations.CreateDirectory(dirPath);

            // Assert
            Assert.True(_mockFileSystem.Directory.Exists(dirPath));
        }

        [Fact]
        public void DeleteEmptyDirectories_ShouldDeleteOnlyEmptyDirectories()
        {
            // Arrange
            _mockFileSystem.AddDirectory("C:\\root");
            _mockFileSystem.AddDirectory("C:\\root\\empty1");
            _mockFileSystem.AddDirectory("C:\\root\\empty2\\empty3"); // empty3 inside empty2
            _mockFileSystem.AddDirectory("C:\\root\\notempty");
            _mockFileSystem.AddFile("C:\\root\\notempty\\file.txt", new MockFileData("content"));
            _mockFileSystem.AddDirectory("C:\\root\\anotherempty");

            // Act
            _fileSystemOperations.DeleteEmptyDirectories("C:\\root");

            // Assert
            Assert.False(_mockFileSystem.Directory.Exists("C:\\root\\empty1"));
            Assert.False(_mockFileSystem.Directory.Exists("C:\\root\\empty2\\empty3"));
            Assert.False(_mockFileSystem.Directory.Exists("C:\\root\\empty2"));
            Assert.True(_mockFileSystem.Directory.Exists("C:\\root\\notempty"));
            Assert.True(_mockFileSystem.FileExists("C:\\root\\notempty\\file.txt"));
            Assert.False(_mockFileSystem.Directory.Exists("C:\\root\\anotherempty"));
            Assert.True(_mockFileSystem.Directory.Exists("C:\\root")); // Root itself should remain if it becomes empty after children are deleted (or if it wasn't empty to begin with)
        }
        
        [Fact]
        public void DeleteEmptyDirectories_ShouldHandleNonExistentRoot()
        {
            // Act
            _fileSystemOperations.DeleteEmptyDirectories("C:\\nonexistent");

            // Assert - No exception should be thrown, and nothing should change.
            Assert.False(_mockFileSystem.Directory.Exists("C:\\nonexistent"));
        }

        [Fact]
        public void DeleteEmptyDirectories_ShouldHandleNullOrEmptyRootPath()
        {
            // Act
            _fileSystemOperations.DeleteEmptyDirectories(null);
            _fileSystemOperations.DeleteEmptyDirectories("");

            // Assert - No exception, no changes.
            // (No specific state to check in mock filesystem for this, just that it doesn't throw)
        }
        
        [Fact]
        public void DeleteEmptyDirectories_LogsWarning_OnIOException()
        {
            // Arrange
            var mockDirectory = new Mock<IDirectory>();
            mockDirectory.Setup(d => d.Exists("C:\\root_io_ex")).Returns(true);
            mockDirectory.Setup(d => d.EnumerateDirectories("C:\\root_io_ex", "*", SearchOption.AllDirectories))
                         .Returns(["C:\\root_io_ex\\empty_dir_io"]);
            mockDirectory.Setup(d => d.EnumerateFileSystemEntries("C:\\root_io_ex\\empty_dir_io"))
                         .Returns([]);
            mockDirectory.Setup(d => d.Delete("C:\\root_io_ex\\empty_dir_io"))
                         .Throws(new IOException("Simulated IO Exception"));

            var customMockFileSystem = new Mock<IFileSystem>();
            customMockFileSystem.Setup(fs => fs.Directory).Returns(mockDirectory.Object);
            customMockFileSystem.Setup(fs => fs.Path).Returns(_mockFileSystem.Path); // Use real path logic

            var fileSystemOpsWithCustomMock = new FileSystemOperations(_mockLogger.Object, customMockFileSystem.Object);
            
            // Act
            fileSystemOpsWithCustomMock.DeleteEmptyDirectories("C:\\root_io_ex");

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Could not delete empty directory C:\\root_io_ex\\empty_dir_io")),
                    It.IsAny<IOException>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }


        [Fact]
        public void FileExists_ShouldReturnTrue_WhenFileExists()
        {
            // Arrange
            string filePath = "C:\\existing_file.txt";
            _mockFileSystem.AddFile(filePath, new MockFileData("content"));

            // Act
            bool exists = _fileSystemOperations.FileExists(filePath);

            // Assert
            Assert.True(exists);
        }

        [Fact]
        public void FileExists_ShouldReturnFalse_WhenFileDoesNotExist()
        {
            // Arrange
            string filePath = "C:\\non_existing_file.txt";

            // Act
            bool exists = _fileSystemOperations.FileExists(filePath);

            // Assert
            Assert.False(exists);
        }

        [Fact]
        public void DirectoryExists_ShouldReturnTrue_WhenDirectoryExists()
        {
            // Arrange
            string dirPath = "C:\\existing_dir";
            _mockFileSystem.AddDirectory(dirPath);

            // Act
            bool exists = _fileSystemOperations.DirectoryExists(dirPath);

            // Assert
            Assert.True(exists);
        }

        [Fact]
        public void DirectoryExists_ShouldReturnFalse_WhenDirectoryDoesNotExist()
        {
            // Arrange
            string dirPath = "C:\\non_existing_dir";

            // Act
            bool exists = _fileSystemOperations.DirectoryExists(dirPath);

            // Assert
            Assert.False(exists);
        }

        [Fact]
        public void GetFileInfo_ShouldReturnFileInfo()
        {
            // Arrange
            string filePath = "C:\\file_for_info.txt";
            _mockFileSystem.AddFile(filePath, new MockFileData("info content"));

            // Act
            IFileInfo fileInfo = _fileSystemOperations.GetFileInfo(filePath); // IFileInfo from System.IO.Abstractions

            // Assert
            Assert.NotNull(fileInfo);
            Assert.Equal(filePath, fileInfo.FullName);
            Assert.True(fileInfo.Exists);
        }

        [Fact]
        public void EnumerateFiles_ShouldReturnMatchingFiles()
        {
            // Arrange
            _mockFileSystem.AddFile("C:\\search_dir\\file1.txt", new MockFileData(""));
            _mockFileSystem.AddFile("C:\\search_dir\\file2.log", new MockFileData(""));
            _mockFileSystem.AddFile("C:\\search_dir\\sub\\file3.txt", new MockFileData(""));

            // Act
            var files = _fileSystemOperations.EnumerateFiles("C:\\search_dir", "*.txt", SearchOption.AllDirectories).ToList();

            // Assert
            Assert.Contains("C:\\search_dir\\file1.txt", files);
            Assert.Contains("C:\\search_dir\\sub\\file3.txt", files);
            Assert.DoesNotContain("C:\\search_dir\\file2.log", files);
            Assert.Equal(2, files.Count);
        }
        
        [Fact]
        public void SetLastWriteTimeUtc_ShouldSetTime()
        {
            // Arrange
            string filePath = "C:\\file_to_set_time.txt";
            var initialTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var newTime = new DateTime(2023, 10, 26, 12, 0, 0, DateTimeKind.Utc);
            _mockFileSystem.AddFile(filePath, new MockFileData("content") { LastWriteTime = initialTime });

            // Act
            _fileSystemOperations.SetLastWriteTimeUtc(filePath, newTime);

            // Assert
            Assert.Equal(newTime, _mockFileSystem.GetFile(filePath).LastWriteTime.UtcDateTime);
        }

        [Fact]
        public void SetLastWriteTimeUtc_ShouldRetry_OnIOException()
        {
            // Arrange
            string filePath = "C:\\file_set_time_retry.txt";
            var newTime = DateTime.UtcNow;
            
            var mockFile = new Mock<IFile>();
            var attempt = 0;
            mockFile.Setup(f => f.SetLastWriteTimeUtc(filePath, newTime))
                .Callback(() => {
                    if (attempt < 2)
                    {
                        attempt++;
                        throw new IOException("Simulated IO error on set time");
                    }
                    // On successful attempt, we'd actually set it on a mock file data if we were using MockFileSystem directly here.
                    // For this isolated mock, we just let it pass after enough attempts.
                });

            var customMockFileSystem = new Mock<IFileSystem>();
            customMockFileSystem.Setup(fs => fs.File).Returns(mockFile.Object);
            // Need to ensure other parts of IFileSystem are available if used by the method or retry policy, though not directly by SetLastWriteTimeUtc
            customMockFileSystem.Setup(fs => fs.Path).Returns(_mockFileSystem.Path);
            customMockFileSystem.Setup(fs => fs.Directory).Returns(_mockFileSystem.Directory);


            var fileSystemOpsWithCustomMock = new FileSystemOperations(_mockLogger.Object, customMockFileSystem.Object);

            // Act
            fileSystemOpsWithCustomMock.SetLastWriteTimeUtc(filePath, newTime);

            // Assert
            mockFile.Verify(f => f.SetLastWriteTimeUtc(filePath, newTime), Times.Exactly(3));
        }
    }
}