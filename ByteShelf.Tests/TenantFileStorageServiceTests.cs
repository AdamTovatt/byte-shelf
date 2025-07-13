using ByteShelf.Services;
using ByteShelfCommon;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Text;
using System.Text.Json;

namespace ByteShelf.Tests
{
    [TestClass]
    public class FileStorageServiceTests
    {
        private string _tempStoragePath = null!;
        private Mock<IStorageService> _mockStorageService = null!;
        private TestLogger<FileStorageService> _logger = null!;
        private FileStorageService _service = null!;
        private JsonSerializerOptions _jsonOptions = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempStoragePath = Path.Combine(Path.GetTempPath(), $"ByteShelf-FileStorage-Test-{Guid.NewGuid()}");
            _mockStorageService = new Mock<IStorageService>();
            _logger = new TestLogger<FileStorageService>();

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
            };

            _service = new FileStorageService(_tempStoragePath, _mockStorageService.Object, _logger);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempStoragePath))
            {
                Directory.Delete(_tempStoragePath, true);
            }
        }

        [TestMethod]
        public void Constructor_WithNullStoragePath_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new FileStorageService(null!, _mockStorageService.Object, _logger));
        }

        [TestMethod]
        public void Constructor_WithNullStorageService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new FileStorageService(_tempStoragePath, null!, _logger));
        }

        [TestMethod]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new FileStorageService(_tempStoragePath, _mockStorageService.Object, null!));
        }

        [TestMethod]
        public async Task GetFilesAsync_WhenNoFilesExist_ReturnsEmptyList()
        {
            // Act
            IEnumerable<ShelfFileMetadata> result = await _service.GetFilesAsync("tenant1");

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count());
        }

        [TestMethod]
        public async Task GetFilesAsync_WhenFilesExist_ReturnsAllFiles()
        {
            // Arrange
            string tenantId = "tenant1";
            string tenantMetadataPath = Path.Combine(_tempStoragePath, tenantId, "metadata");
            Directory.CreateDirectory(tenantMetadataPath);

            ShelfFileMetadata file1 = new ShelfFileMetadata(Guid.NewGuid(), "test1.txt", "text/plain", 1024, new List<Guid> { Guid.NewGuid() });
            ShelfFileMetadata file2 = new ShelfFileMetadata(Guid.NewGuid(), "test2.txt", "text/plain", 2048, new List<Guid> { Guid.NewGuid(), Guid.NewGuid() });

            await File.WriteAllTextAsync(Path.Combine(tenantMetadataPath, $"{file1.Id}.json"), JsonSerializer.Serialize(file1, _jsonOptions));
            await File.WriteAllTextAsync(Path.Combine(tenantMetadataPath, $"{file2.Id}.json"), JsonSerializer.Serialize(file2, _jsonOptions));

            // Act
            IEnumerable<ShelfFileMetadata> result = await _service.GetFilesAsync(tenantId);

            // Assert
            List<ShelfFileMetadata> resultList = result.ToList();
            Assert.AreEqual(2, resultList.Count);
            Assert.IsTrue(resultList.Any(f => f.Id == file1.Id));
            Assert.IsTrue(resultList.Any(f => f.Id == file2.Id));
        }

        [TestMethod]
        public async Task GetFilesAsync_WithInvalidMetadataFile_LogsWarningAndContinues()
        {
            // Arrange
            string tenantId = "tenant1";
            string tenantMetadataPath = Path.Combine(_tempStoragePath, tenantId, "metadata");
            Directory.CreateDirectory(tenantMetadataPath);

            ShelfFileMetadata validFile = new ShelfFileMetadata(Guid.NewGuid(), "valid.txt", "text/plain", 1024, new List<Guid> { Guid.NewGuid() });
            await File.WriteAllTextAsync(Path.Combine(tenantMetadataPath, $"{validFile.Id}.json"), JsonSerializer.Serialize(validFile, _jsonOptions));

            // Create invalid JSON file
            await File.WriteAllTextAsync(Path.Combine(tenantMetadataPath, "invalid.json"), "invalid json content");

            // Act
            IEnumerable<ShelfFileMetadata> result = await _service.GetFilesAsync(tenantId);

            // Assert
            List<ShelfFileMetadata> resultList = result.ToList();
            Assert.AreEqual(1, resultList.Count);
            Assert.AreEqual(validFile.Id, resultList[0].Id);
        }

        [TestMethod]
        public async Task GetFileMetadataAsync_WhenFileExists_ReturnsMetadata()
        {
            // Arrange
            string tenantId = "tenant1";
            string tenantMetadataPath = Path.Combine(_tempStoragePath, tenantId, "metadata");
            Directory.CreateDirectory(tenantMetadataPath);

            ShelfFileMetadata expectedMetadata = new ShelfFileMetadata(Guid.NewGuid(), "test.txt", "text/plain", 1024, new List<Guid> { Guid.NewGuid() });
            await File.WriteAllTextAsync(Path.Combine(tenantMetadataPath, $"{expectedMetadata.Id}.json"), JsonSerializer.Serialize(expectedMetadata, _jsonOptions));

            // Act
            ShelfFileMetadata? result = await _service.GetFileMetadataAsync(tenantId, expectedMetadata.Id);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(expectedMetadata.Id, result.Id);
            Assert.AreEqual(expectedMetadata.OriginalFilename, result.OriginalFilename);
        }

        [TestMethod]
        public async Task GetFileMetadataAsync_WhenFileDoesNotExist_ReturnsNull()
        {
            // Act
            ShelfFileMetadata? result = await _service.GetFileMetadataAsync("tenant1", Guid.NewGuid());

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task GetChunkAsync_WhenChunkExists_ReturnsStream()
        {
            // Arrange
            string tenantId = "tenant1";
            Guid chunkId = Guid.NewGuid();
            string tenantBinPath = Path.Combine(_tempStoragePath, tenantId, "bin");
            Directory.CreateDirectory(tenantBinPath);

            string chunkContent = "chunk data";
            await File.WriteAllTextAsync(Path.Combine(tenantBinPath, $"{chunkId}.bin"), chunkContent);

            // Act
            Stream result = await _service.GetChunkAsync(tenantId, chunkId);

            // Assert
            Assert.IsNotNull(result);
            using StreamReader reader = new StreamReader(result);
            string content = await reader.ReadToEndAsync();
            Assert.AreEqual(chunkContent, content);
        }

        [TestMethod]
        public async Task GetChunkAsync_WhenChunkDoesNotExist_ThrowsFileNotFoundException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(
                () => _service.GetChunkAsync("tenant1", Guid.NewGuid()));
        }

        [TestMethod]
        public async Task SaveChunkAsync_WhenQuotaAllows_SavesChunkAndRecordsUsage()
        {
            // Arrange
            string tenantId = "tenant1";
            Guid chunkId = Guid.NewGuid();
            string chunkContent = "test chunk data";
            using MemoryStream chunkStream = new MemoryStream(Encoding.UTF8.GetBytes(chunkContent));

            _mockStorageService.Setup(s => s.CanStoreData(tenantId, chunkContent.Length))
                .Returns(true);

            // Act
            Guid result = await _service.SaveChunkAsync(tenantId, chunkId, chunkStream);

            // Assert
            Assert.AreEqual(chunkId, result);
            _mockStorageService.Verify(s => s.CanStoreData(tenantId, chunkContent.Length), Times.Once);
            _mockStorageService.Verify(s => s.RecordStorageUsed(tenantId, chunkContent.Length), Times.Once);

            // Verify file was saved
            string expectedPath = Path.Combine(_tempStoragePath, tenantId, "bin", $"{chunkId}.bin");
            Assert.IsTrue(File.Exists(expectedPath));
            string savedContent = await File.ReadAllTextAsync(expectedPath);
            Assert.AreEqual(chunkContent, savedContent);
        }

        [TestMethod]
        public async Task SaveChunkAsync_WhenQuotaExceeded_ThrowsInvalidOperationException()
        {
            // Arrange
            string tenantId = "tenant1";
            Guid chunkId = Guid.NewGuid();
            string chunkContent = "test chunk data";
            using MemoryStream chunkStream = new MemoryStream(Encoding.UTF8.GetBytes(chunkContent));

            _mockStorageService.Setup(s => s.CanStoreData(tenantId, chunkContent.Length))
                .Returns(false);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _service.SaveChunkAsync(tenantId, chunkId, chunkStream));

            _mockStorageService.Verify(s => s.CanStoreData(tenantId, chunkContent.Length), Times.Once);
            _mockStorageService.Verify(s => s.RecordStorageUsed(It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        }

        [TestMethod]
        public async Task SaveChunkAsync_WithNullStream_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => _service.SaveChunkAsync("tenant1", Guid.NewGuid(), null!));
        }

        [TestMethod]
        public async Task SaveFileMetadataAsync_SavesMetadataFile()
        {
            // Arrange
            string tenantId = "tenant1";
            ShelfFileMetadata metadata = new ShelfFileMetadata(Guid.NewGuid(), "test.txt", "text/plain", 1024, new List<Guid> { Guid.NewGuid() });

            // Act
            await _service.SaveFileMetadataAsync(tenantId, metadata);

            // Assert
            string expectedPath = Path.Combine(_tempStoragePath, tenantId, "metadata", $"{metadata.Id}.json");
            Assert.IsTrue(File.Exists(expectedPath));

            string savedJson = await File.ReadAllTextAsync(expectedPath);
            ShelfFileMetadata? savedMetadata = JsonSerializer.Deserialize<ShelfFileMetadata>(savedJson, _jsonOptions);
            Assert.IsNotNull(savedMetadata);
            Assert.AreEqual(metadata.Id, savedMetadata.Id);
        }

        [TestMethod]
        public async Task SaveFileMetadataAsync_WithNullMetadata_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => _service.SaveFileMetadataAsync("tenant1", null!));
        }

        [TestMethod]
        public async Task DeleteFileAsync_WhenFileExists_DeletesChunksAndMetadata()
        {
            // Arrange
            string tenantId = "tenant1";
            Guid fileId = Guid.NewGuid();
            Guid chunkId1 = Guid.NewGuid();
            Guid chunkId2 = Guid.NewGuid();

            ShelfFileMetadata metadata = new ShelfFileMetadata(fileId, "test.txt", "text/plain", 2048, new List<Guid> { chunkId1, chunkId2 });

            string tenantMetadataPath = Path.Combine(_tempStoragePath, tenantId, "metadata");
            string tenantBinPath = Path.Combine(_tempStoragePath, tenantId, "bin");
            Directory.CreateDirectory(tenantMetadataPath);
            Directory.CreateDirectory(tenantBinPath);

            // Create metadata file
            await File.WriteAllTextAsync(Path.Combine(tenantMetadataPath, $"{fileId}.json"), JsonSerializer.Serialize(metadata, _jsonOptions));

            // Create chunk files
            await File.WriteAllTextAsync(Path.Combine(tenantBinPath, $"{chunkId1}.bin"), "chunk1");
            await File.WriteAllTextAsync(Path.Combine(tenantBinPath, $"{chunkId2}.bin"), "chunk2");

            // Act
            await _service.DeleteFileAsync(tenantId, fileId);

            // Assert
            Assert.IsFalse(File.Exists(Path.Combine(tenantMetadataPath, $"{fileId}.json")));
            Assert.IsFalse(File.Exists(Path.Combine(tenantBinPath, $"{chunkId1}.bin")));
            Assert.IsFalse(File.Exists(Path.Combine(tenantBinPath, $"{chunkId2}.bin")));

            _mockStorageService.Verify(s => s.RecordStorageFreed(tenantId, It.IsAny<long>()), Times.Once);
        }

        [TestMethod]
        public async Task DeleteFileAsync_WhenFileDoesNotExist_DoesNothing()
        {
            // Act
            await _service.DeleteFileAsync("tenant1", Guid.NewGuid());

            // Assert
            _mockStorageService.Verify(s => s.RecordStorageFreed(It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        }

        [TestMethod]
        public async Task DeleteAllFilesAsync_WhenNoFilesExist_ReturnsZero()
        {
            // Act
            int deletedCount = await _service.DeleteAllFilesAsync("tenant1");

            // Assert
            Assert.AreEqual(0, deletedCount);
            _mockStorageService.Verify(s => s.RecordStorageFreed(It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        }

        [TestMethod]
        public async Task DeleteAllFilesAsync_WhenFilesExist_DeletesAllFilesAndChunks()
        {
            // Arrange
            string tenantId = "tenant1";
            Guid fileId1 = Guid.NewGuid();
            Guid fileId2 = Guid.NewGuid();
            Guid chunkId1 = Guid.NewGuid();
            Guid chunkId2 = Guid.NewGuid();
            Guid chunkId3 = Guid.NewGuid();

            ShelfFileMetadata metadata1 = new ShelfFileMetadata(fileId1, "test1.txt", "text/plain", 1024, new List<Guid> { chunkId1, chunkId2 });
            ShelfFileMetadata metadata2 = new ShelfFileMetadata(fileId2, "test2.txt", "text/plain", 2048, new List<Guid> { chunkId3 });

            string tenantMetadataPath = Path.Combine(_tempStoragePath, tenantId, "metadata");
            string tenantBinPath = Path.Combine(_tempStoragePath, tenantId, "bin");
            Directory.CreateDirectory(tenantMetadataPath);
            Directory.CreateDirectory(tenantBinPath);

            // Create metadata files
            await File.WriteAllTextAsync(Path.Combine(tenantMetadataPath, $"{fileId1}.json"), JsonSerializer.Serialize(metadata1, _jsonOptions));
            await File.WriteAllTextAsync(Path.Combine(tenantMetadataPath, $"{fileId2}.json"), JsonSerializer.Serialize(metadata2, _jsonOptions));

            // Create chunk files
            await File.WriteAllTextAsync(Path.Combine(tenantBinPath, $"{chunkId1}.bin"), "chunk1");
            await File.WriteAllTextAsync(Path.Combine(tenantBinPath, $"{chunkId2}.bin"), "chunk2");
            await File.WriteAllTextAsync(Path.Combine(tenantBinPath, $"{chunkId3}.bin"), "chunk3");

            // Act
            int deletedCount = await _service.DeleteAllFilesAsync(tenantId);

            // Assert
            Assert.AreEqual(2, deletedCount);
            Assert.IsFalse(File.Exists(Path.Combine(tenantMetadataPath, $"{fileId1}.json")));
            Assert.IsFalse(File.Exists(Path.Combine(tenantMetadataPath, $"{fileId2}.json")));
            Assert.IsFalse(File.Exists(Path.Combine(tenantBinPath, $"{chunkId1}.bin")));
            Assert.IsFalse(File.Exists(Path.Combine(tenantBinPath, $"{chunkId2}.bin")));
            Assert.IsFalse(File.Exists(Path.Combine(tenantBinPath, $"{chunkId3}.bin")));

            _mockStorageService.Verify(s => s.RecordStorageFreed(tenantId, It.IsAny<long>()), Times.Once);
        }

        [TestMethod]
        public async Task DeleteAllFilesAsync_WhenMetadataFileIsCorrupted_ContinuesWithOtherFiles()
        {
            // Arrange
            string tenantId = "tenant1";
            Guid fileId1 = Guid.NewGuid();
            Guid fileId2 = Guid.NewGuid();
            Guid chunkId1 = Guid.NewGuid();
            Guid chunkId2 = Guid.NewGuid();

            ShelfFileMetadata metadata1 = new ShelfFileMetadata(fileId1, "test1.txt", "text/plain", 1024, new List<Guid> { chunkId1 });
            ShelfFileMetadata metadata2 = new ShelfFileMetadata(fileId2, "test2.txt", "text/plain", 2048, new List<Guid> { chunkId2 });

            string tenantMetadataPath = Path.Combine(_tempStoragePath, tenantId, "metadata");
            string tenantBinPath = Path.Combine(_tempStoragePath, tenantId, "bin");
            Directory.CreateDirectory(tenantMetadataPath);
            Directory.CreateDirectory(tenantBinPath);

            // Create valid metadata file
            await File.WriteAllTextAsync(Path.Combine(tenantMetadataPath, $"{fileId1}.json"), JsonSerializer.Serialize(metadata1, _jsonOptions));
            
            // Create corrupted metadata file
            await File.WriteAllTextAsync(Path.Combine(tenantMetadataPath, $"{fileId2}.json"), "invalid json content");

            // Create chunk files
            await File.WriteAllTextAsync(Path.Combine(tenantBinPath, $"{chunkId1}.bin"), "chunk1");
            await File.WriteAllTextAsync(Path.Combine(tenantBinPath, $"{chunkId2}.bin"), "chunk2");

            // Act
            int deletedCount = await _service.DeleteAllFilesAsync(tenantId);

            // Assert
            Assert.AreEqual(1, deletedCount); // Only the valid file should be deleted
            Assert.IsFalse(File.Exists(Path.Combine(tenantMetadataPath, $"{fileId1}.json")));
            Assert.IsFalse(File.Exists(Path.Combine(tenantBinPath, $"{chunkId1}.bin")));
            // Corrupted file should still exist
            Assert.IsTrue(File.Exists(Path.Combine(tenantMetadataPath, $"{fileId2}.json")));
            Assert.IsTrue(File.Exists(Path.Combine(tenantBinPath, $"{chunkId2}.bin")));

            _mockStorageService.Verify(s => s.RecordStorageFreed(tenantId, It.IsAny<long>()), Times.Once);
        }

        [TestMethod]
        public void CanStoreFile_DelegatesToTenantStorageService()
        {
            // Arrange
            string tenantId = "tenant1";
            long fileSize = 1024;

            _mockStorageService.Setup(s => s.CanStoreData(tenantId, fileSize))
                .Returns(true);

            // Act
            bool result = _service.CanStoreFile(tenantId, fileSize);

            // Assert
            Assert.IsTrue(result);
            _mockStorageService.Verify(s => s.CanStoreData(tenantId, fileSize), Times.Once);
        }

        [TestMethod]
        public void CanStoreFile_WithNullTenantId_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() => _service.CanStoreFile(null!, 1024));
        }

        [TestMethod]
        public void CanStoreFile_WithEmptyTenantId_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentException>(() => _service.CanStoreFile("", 1024));
        }

        [TestMethod]
        public void CanStoreFile_WithWhitespaceTenantId_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentException>(() => _service.CanStoreFile("   ", 1024));
        }

        [TestMethod]
        public async Task GetFilesAsync_WithNullTenantId_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => _service.GetFilesAsync(null!));
        }

        [TestMethod]
        public async Task GetFileMetadataAsync_WithNullTenantId_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => _service.GetFileMetadataAsync(null!, Guid.NewGuid()));
        }

        [TestMethod]
        public async Task GetChunkAsync_WithNullTenantId_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => _service.GetChunkAsync(null!, Guid.NewGuid()));
        }

        [TestMethod]
        public async Task SaveChunkAsync_WithNullTenantId_ThrowsArgumentNullException()
        {
            // Arrange
            using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes("test"));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => _service.SaveChunkAsync(null!, Guid.NewGuid(), stream));
        }

        [TestMethod]
        public async Task SaveFileMetadataAsync_WithNullTenantId_ThrowsArgumentNullException()
        {
            // Arrange
            ShelfFileMetadata metadata = new ShelfFileMetadata(Guid.NewGuid(), "test.txt", "text/plain", 1024, new List<Guid>());

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => _service.SaveFileMetadataAsync(null!, metadata));
        }

        [TestMethod]
        public async Task DeleteFileAsync_WithNullTenantId_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => _service.DeleteFileAsync(null!, Guid.NewGuid()));
        }

        private class TestLogger<T> : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                // Test logger - just ignore logs
            }
        }
    }
}