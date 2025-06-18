using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ByteShelfCommon;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ByteShelfCommon.Tests
{
    [TestClass]
    public class ShelfFileTests
    {
        [TestMethod]
        public void Constructor_WithValidParameters_CreatesValidShelfFile()
        {
            // Arrange
            Guid fileId = Guid.NewGuid();
            string originalFilename = "test.txt";
            string contentType = "text/plain";
            long fileSize = 1024;
            List<Guid> chunkIds = new List<Guid> { Guid.NewGuid() };

            ShelfFileMetadata metadata = new ShelfFileMetadata(
                fileId,
                originalFilename,
                contentType,
                fileSize,
                chunkIds);

            TestContentProvider contentProvider = new TestContentProvider("Hello World!");

            // Act
            ShelfFile shelfFile = new ShelfFile(metadata, contentProvider);

            // Assert
            Assert.AreEqual(metadata, shelfFile.Metadata);
            Assert.IsNotNull(shelfFile.GetContentStream());
        }

        [TestMethod]
        public void GetContentStream_ReturnsStreamFromContentProvider()
        {
            // Arrange
            string expectedContent = "Test content for streaming";
            ShelfFileMetadata metadata = CreateTestMetadata();
            TestContentProvider contentProvider = new TestContentProvider(expectedContent);
            ShelfFile shelfFile = new ShelfFile(metadata, contentProvider);

            // Act
            using Stream contentStream = shelfFile.GetContentStream();
            using StreamReader reader = new StreamReader(contentStream);

            // Assert
            string actualContent = reader.ReadToEnd();
            Assert.AreEqual(expectedContent, actualContent);
        }

        [TestMethod]
        public void Dispose_DisposesContentProvider()
        {
            // Arrange
            ShelfFileMetadata metadata = CreateTestMetadata();
            TestContentProvider contentProvider = new TestContentProvider("test");
            ShelfFile shelfFile = new ShelfFile(metadata, contentProvider);

            // Act
            shelfFile.Dispose();

            // Assert
            Assert.IsTrue(contentProvider.IsDisposed);
        }

        [TestMethod]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            // Arrange
            ShelfFileMetadata metadata = CreateTestMetadata();
            TestContentProvider contentProvider = new TestContentProvider("test");
            ShelfFile shelfFile = new ShelfFile(metadata, contentProvider);

            // Act & Assert - Should not throw
            shelfFile.Dispose();
            shelfFile.Dispose();
        }

        private static ShelfFileMetadata CreateTestMetadata()
        {
            return new ShelfFileMetadata(
                Guid.NewGuid(),
                "test.txt",
                "text/plain",
                1024,
                new List<Guid> { Guid.NewGuid() });
        }

        private class TestContentProvider : IContentProvider, IDisposable
        {
            private readonly string _content;
            private bool _isDisposed;

            public TestContentProvider(string content)
            {
                _content = content;
            }

            public Stream GetStream()
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(nameof(TestContentProvider));

                byte[] bytes = Encoding.UTF8.GetBytes(_content);
                return new MemoryStream(bytes);
            }

            public void Dispose()
            {
                _isDisposed = true;
            }

            public bool IsDisposed => _isDisposed;
        }
    }
} 