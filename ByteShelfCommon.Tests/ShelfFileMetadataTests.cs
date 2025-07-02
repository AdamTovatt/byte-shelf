using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace ByteShelfCommon.Tests
{
    [TestClass]
    public class ShelfFileMetadataTests
    {
        [TestMethod]
        public void Constructor_WithValidParameters_CreatesValidMetadata()
        {
            // Arrange
            Guid fileId = Guid.NewGuid();
            string originalFilename = "test.txt";
            string contentType = "text/plain";
            long fileSize = 1024;
            List<Guid> chunkIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

            // Act
            ShelfFileMetadata metadata = new ShelfFileMetadata(
                fileId,
                originalFilename,
                contentType,
                fileSize,
                chunkIds);

            // Assert
            Assert.AreEqual(fileId, metadata.Id);
            Assert.AreEqual(originalFilename, metadata.OriginalFilename);
            Assert.AreEqual(contentType, metadata.ContentType);
            Assert.AreEqual(fileSize, metadata.FileSize);
            Assert.AreEqual(chunkIds, metadata.ChunkIds);
            Assert.IsTrue(metadata.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
            Assert.IsTrue(metadata.CreatedAt <= DateTimeOffset.UtcNow);
        }

        [TestMethod]
        public void JsonSerialization_CanSerializeAndDeserialize()
        {
            // Arrange
            Guid fileId = Guid.NewGuid();
            string originalFilename = "test.txt";
            string contentType = "text/plain";
            long fileSize = 1024;
            List<Guid> chunkIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

            ShelfFileMetadata originalMetadata = new ShelfFileMetadata(
                fileId,
                originalFilename,
                contentType,
                fileSize,
                chunkIds);

            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };

            // Act
            string json = JsonSerializer.Serialize(originalMetadata, options);
            ShelfFileMetadata? deserializedMetadata = JsonSerializer.Deserialize<ShelfFileMetadata>(json, options);

            // Assert
            Assert.IsNotNull(deserializedMetadata);
            Assert.AreEqual(originalMetadata.Id, deserializedMetadata.Id);
            Assert.AreEqual(originalMetadata.OriginalFilename, deserializedMetadata.OriginalFilename);
            Assert.AreEqual(originalMetadata.ContentType, deserializedMetadata.ContentType);
            Assert.AreEqual(originalMetadata.FileSize, deserializedMetadata.FileSize);
            Assert.AreEqual(originalMetadata.ChunkIds.Count, deserializedMetadata.ChunkIds.Count);
            Assert.AreEqual(originalMetadata.CreatedAt, deserializedMetadata.CreatedAt);
        }

        [TestMethod]
        public void JsonSerialization_WithEmptyChunkList_WorksCorrectly()
        {
            // Arrange
            Guid fileId = Guid.NewGuid();
            string originalFilename = "empty.txt";
            string contentType = "text/plain";
            long fileSize = 0;
            List<Guid> chunkIds = new List<Guid>();

            ShelfFileMetadata originalMetadata = new ShelfFileMetadata(
                fileId,
                originalFilename,
                contentType,
                fileSize,
                chunkIds);

            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };

            // Act
            string json = JsonSerializer.Serialize(originalMetadata, options);
            ShelfFileMetadata? deserializedMetadata = JsonSerializer.Deserialize<ShelfFileMetadata>(json, options);

            // Assert
            Assert.IsNotNull(deserializedMetadata);
            Assert.AreEqual(0, deserializedMetadata.ChunkIds.Count);
        }
    }
}