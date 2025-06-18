using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ByteShelf.Services;
using ByteShelfCommon;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ByteShelf.Tests
{
    [TestClass]
    public class FileStorageServiceTests
    {
        private string _tempStoragePath = null!;
        private FileStorageService _service = null!;
        private TestLogger<FileStorageService> _logger = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempStoragePath = Path.Combine(Path.GetTempPath(), $"ByteShelf-Test-{Guid.NewGuid()}");
            _logger = new TestLogger<FileStorageService>();
            _service = new FileStorageService(_tempStoragePath, _logger);
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
        public void Constructor_CreatesStorageDirectories()
        {
            // Assert
            Assert.IsTrue(Directory.Exists(Path.Combine(_tempStoragePath, "metadata")));
            Assert.IsTrue(Directory.Exists(Path.Combine(_tempStoragePath, "bin")));
        }

        [TestMethod]
        public async Task SaveFileMetadataAsync_And_GetFileMetadataAsync_WorkCorrectly()
        {
            // Arrange
            Guid fileId = Guid.NewGuid();
            ShelfFileMetadata metadata = new ShelfFileMetadata(
                fileId,
                "test.txt",
                "text/plain",
                1024,
                new List<Guid> { Guid.NewGuid(), Guid.NewGuid() });

            // Act
            await _service.SaveFileMetadataAsync(metadata, CancellationToken.None);
            ShelfFileMetadata? retrievedMetadata = await _service.GetFileMetadataAsync(fileId, CancellationToken.None);

            // Assert
            Assert.IsNotNull(retrievedMetadata);
            Assert.AreEqual(metadata.Id, retrievedMetadata.Id);
            Assert.AreEqual(metadata.OriginalFilename, retrievedMetadata.OriginalFilename);
            Assert.AreEqual(metadata.ContentType, retrievedMetadata.ContentType);
            Assert.AreEqual(metadata.FileSize, retrievedMetadata.FileSize);
            Assert.AreEqual(metadata.ChunkIds.Count, retrievedMetadata.ChunkIds.Count);
        }

        [TestMethod]
        public async Task GetFileMetadataAsync_WhenFileDoesNotExist_ReturnsNull()
        {
            // Arrange
            Guid nonExistentFileId = Guid.NewGuid();

            // Act
            ShelfFileMetadata? result = await _service.GetFileMetadataAsync(nonExistentFileId, CancellationToken.None);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task SaveChunkAsync_And_GetChunkAsync_WorkCorrectly()
        {
            // Arrange
            Guid chunkId = Guid.NewGuid();
            string expectedContent = "Test chunk content";
            using MemoryStream chunkData = new MemoryStream(Encoding.UTF8.GetBytes(expectedContent));

            // Act
            Guid savedChunkId = await _service.SaveChunkAsync(chunkId, chunkData, CancellationToken.None);
            Stream retrievedChunkStream = await _service.GetChunkAsync(chunkId, CancellationToken.None);

            // Assert
            Assert.AreEqual(chunkId, savedChunkId);
            using StreamReader reader = new StreamReader(retrievedChunkStream);
            string actualContent = reader.ReadToEnd();
            Assert.AreEqual(expectedContent, actualContent);
        }

        [TestMethod]
        public async Task GetChunkAsync_WhenChunkDoesNotExist_ThrowsFileNotFoundException()
        {
            // Arrange
            Guid nonExistentChunkId = Guid.NewGuid();

            // Act & Assert
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(
                () => _service.GetChunkAsync(nonExistentChunkId, CancellationToken.None));
        }

        [TestMethod]
        public async Task GetFilesAsync_ReturnsAllSavedFiles()
        {
            // Arrange
            ShelfFileMetadata metadata1 = new ShelfFileMetadata(
                Guid.NewGuid(),
                "file1.txt",
                "text/plain",
                1024,
                new List<Guid> { Guid.NewGuid() });

            ShelfFileMetadata metadata2 = new ShelfFileMetadata(
                Guid.NewGuid(),
                "file2.txt",
                "text/plain",
                2048,
                new List<Guid> { Guid.NewGuid(), Guid.NewGuid() });

            await _service.SaveFileMetadataAsync(metadata1, CancellationToken.None);
            await _service.SaveFileMetadataAsync(metadata2, CancellationToken.None);

            // Act
            IEnumerable<ShelfFileMetadata> files = await _service.GetFilesAsync(CancellationToken.None);

            // Assert
            List<ShelfFileMetadata> fileList = files.ToList();
            Assert.AreEqual(2, fileList.Count);
            Assert.IsTrue(fileList.Any(f => f.OriginalFilename == "file1.txt"));
            Assert.IsTrue(fileList.Any(f => f.OriginalFilename == "file2.txt"));
        }

        [TestMethod]
        public async Task DeleteFileAsync_RemovesFileAndChunks()
        {
            // Arrange
            Guid fileId = Guid.NewGuid();
            Guid chunkId1 = Guid.NewGuid();
            Guid chunkId2 = Guid.NewGuid();

            ShelfFileMetadata metadata = new ShelfFileMetadata(
                fileId,
                "test.txt",
                "text/plain",
                2048,
                new List<Guid> { chunkId1, chunkId2 });

            // Save metadata and chunks
            await _service.SaveFileMetadataAsync(metadata, CancellationToken.None);
            using (MemoryStream chunk1 = new MemoryStream(Encoding.UTF8.GetBytes("chunk1")))
            using (MemoryStream chunk2 = new MemoryStream(Encoding.UTF8.GetBytes("chunk2")))
            {
                await _service.SaveChunkAsync(chunkId1, chunk1, CancellationToken.None);
                await _service.SaveChunkAsync(chunkId2, chunk2, CancellationToken.None);
            }

            // Act
            await _service.DeleteFileAsync(fileId, CancellationToken.None);

            // Assert
            ShelfFileMetadata? retrievedMetadata = await _service.GetFileMetadataAsync(fileId, CancellationToken.None);
            Assert.IsNull(retrievedMetadata);

            // Verify chunks are also deleted
            Assert.IsFalse(File.Exists(Path.Combine(_tempStoragePath, "bin", $"{chunkId1}.bin")));
            Assert.IsFalse(File.Exists(Path.Combine(_tempStoragePath, "bin", $"{chunkId2}.bin")));
        }

        [TestMethod]
        public async Task DeleteFileAsync_WhenFileDoesNotExist_DoesNotThrow()
        {
            // Arrange
            Guid nonExistentFileId = Guid.NewGuid();

            // Act & Assert - Should not throw
            await _service.DeleteFileAsync(nonExistentFileId, CancellationToken.None);
        }

        [TestMethod]
        public async Task GetFilesAsync_WithCorruptedMetadata_SkipsCorruptedFiles()
        {
            // Arrange
            // Create a valid metadata file
            ShelfFileMetadata validMetadata = new ShelfFileMetadata(
                Guid.NewGuid(),
                "valid.txt",
                "text/plain",
                1024,
                new List<Guid> { Guid.NewGuid() });
            await _service.SaveFileMetadataAsync(validMetadata, CancellationToken.None);

            // Create a corrupted metadata file
            string corruptedMetadataPath = Path.Combine(_tempStoragePath, "metadata", $"{Guid.NewGuid()}.json");
            await File.WriteAllTextAsync(corruptedMetadataPath, "invalid json content");

            // Act
            IEnumerable<ShelfFileMetadata> files = await _service.GetFilesAsync(CancellationToken.None);

            // Assert
            List<ShelfFileMetadata> fileList = files.ToList();
            Assert.AreEqual(1, fileList.Count);
            Assert.AreEqual("valid.txt", fileList[0].OriginalFilename);
        }

        private class TestLogger<T> : ILogger<T>
        {
            public List<string> LogMessages { get; } = new List<string>();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                string message = formatter(state, exception);
                LogMessages.Add($"{logLevel}: {message}");
            }
        }
    }
} 