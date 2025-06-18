using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ByteShelfClient;
using ByteShelfCommon;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ByteShelfClient.Tests
{
    [TestClass]
    public class HttpShelfFileProviderTests
    {
        private HttpShelfFileProvider _provider = null!;
        private HttpClient _httpClient = null!;
        private TestHttpMessageHandler _messageHandler = null!;

        [TestInitialize]
        public void Setup()
        {
            _messageHandler = new TestHttpMessageHandler();
            _httpClient = new HttpClient(_messageHandler);
            _httpClient.BaseAddress = new Uri("http://localhost:5000/");
            _provider = new HttpShelfFileProvider(_httpClient);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _httpClient?.Dispose();
        }

        [TestMethod]
        public async Task GetFilesAsync_ReturnsFilesFromServer()
        {
            // Arrange
            List<ShelfFileMetadata> files = new List<ShelfFileMetadata>
            {
                new ShelfFileMetadata(
                    Guid.NewGuid(),
                    "test1.txt",
                    "text/plain",
                    1024,
                    new List<Guid> { Guid.NewGuid() }),
                new ShelfFileMetadata(
                    Guid.NewGuid(),
                    "test2.txt",
                    "text/plain",
                    2048,
                    new List<Guid> { Guid.NewGuid(), Guid.NewGuid() })
            };

            string jsonResponse = JsonSerializer.Serialize(files);
            _messageHandler.SetupResponse("api/files", jsonResponse);

            // Act
            IEnumerable<ShelfFileMetadata> result = await _provider.GetFilesAsync();

            // Assert
            List<ShelfFileMetadata> resultList = new List<ShelfFileMetadata>(result);
            Assert.AreEqual(2, resultList.Count);
            Assert.AreEqual("test1.txt", resultList[0].OriginalFilename);
            Assert.AreEqual("test2.txt", resultList[1].OriginalFilename);
        }

        [TestMethod]
        public async Task ReadFileAsync_ReturnsShelfFileWithMetadata()
        {
            // Arrange
            Guid fileId = Guid.NewGuid();
            ShelfFileMetadata metadata = new ShelfFileMetadata(
                fileId,
                "test.txt",
                "text/plain",
                1024,
                new List<Guid> { Guid.NewGuid() });

            string metadataJson = JsonSerializer.Serialize(metadata);
            _messageHandler.SetupResponse($"api/files/{fileId}/metadata", metadataJson);
            _messageHandler.SetupResponse($"api/chunks/{metadata.ChunkIds[0]}", "Hello World!");

            // Act
            ShelfFile result = await _provider.ReadFileAsync(fileId);

            // Assert
            Assert.AreEqual(metadata.Id, result.Metadata.Id);
            Assert.AreEqual(metadata.OriginalFilename, result.Metadata.OriginalFilename);
            Assert.IsNotNull(result.GetContentStream());
        }

        [TestMethod]
        public async Task ReadFileAsync_WhenFileNotFound_ThrowsFileNotFoundException()
        {
            // Arrange
            Guid fileId = Guid.NewGuid();
            _messageHandler.SetupResponse($"api/files/{fileId}/metadata", "", HttpStatusCode.NotFound);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(
                () => _provider.ReadFileAsync(fileId));
        }

        [TestMethod]
        public async Task WriteFileAsync_UploadsChunksAndMetadata()
        {
            // Arrange
            string content = "Test file content";
            string filename = "test.txt";
            string contentType = "text/plain";
            using MemoryStream contentStream = new MemoryStream(Encoding.UTF8.GetBytes(content));

            object config = new { ChunkSizeBytes = 1024 };
            string configJson = JsonSerializer.Serialize(config);
            _messageHandler.SetupResponse("api/config/chunk-size", configJson);
            _messageHandler.SetupResponse("api/chunks/*", "OK", HttpStatusCode.OK);
            _messageHandler.SetupResponse("api/files/metadata", "OK", HttpStatusCode.Created);

            // Act
            Guid fileId = await _provider.WriteFileAsync(filename, contentType, contentStream);

            // Assert
            Assert.AreNotEqual(Guid.Empty, fileId);
            Assert.IsTrue(_messageHandler.Requests.Count > 0);
        }

        [TestMethod]
        public async Task WriteFileAsync_WithLargeFile_CreatesMultipleChunks()
        {
            // Arrange
            string largeContent = new string('A', 3000); // Larger than chunk size
            string filename = "large.txt";
            string contentType = "text/plain";
            using MemoryStream contentStream = new MemoryStream(Encoding.UTF8.GetBytes(largeContent));

            object config = new { ChunkSizeBytes = 1024 };
            string configJson = JsonSerializer.Serialize(config);
            _messageHandler.SetupResponse("api/config/chunk-size", configJson);
            _messageHandler.SetupResponse("api/chunks/*", "OK", HttpStatusCode.OK);
            _messageHandler.SetupResponse("api/files/metadata", "OK", HttpStatusCode.Created);

            // Act
            Guid fileId = await _provider.WriteFileAsync(filename, contentType, contentStream);

            // Assert
            Assert.AreNotEqual(Guid.Empty, fileId);
            // Should have multiple chunk upload requests (at least 3 for 3000 bytes with 1024 chunk size)
            int chunkRequests = _messageHandler.Requests.Count(r => r.RequestUri!.PathAndQuery.Contains("api/chunks/"));
            Assert.IsTrue(chunkRequests >= 3);
        }

        [TestMethod]
        public async Task DeleteFileAsync_SendsDeleteRequest()
        {
            // Arrange
            Guid fileId = Guid.NewGuid();
            _messageHandler.SetupResponse($"api/files/{fileId}", "", HttpStatusCode.NoContent);

            // Act
            await _provider.DeleteFileAsync(fileId);

            // Assert
            Assert.IsTrue(_messageHandler.Requests.Count > 0);
        }

        private class TestHttpMessageHandler : HttpMessageHandler
        {
            public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();
            private readonly Dictionary<string, (string Content, HttpStatusCode StatusCode)> _responses = new Dictionary<string, (string, HttpStatusCode)>();

            public void SetupResponse(string url, string content, HttpStatusCode statusCode = HttpStatusCode.OK)
            {
                _responses[url] = (content, statusCode);
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                string url = request.RequestUri!.PathAndQuery;

                // Find matching response
                foreach (KeyValuePair<string, (string Content, HttpStatusCode StatusCode)> response in _responses)
                {
                    if (url.Contains(response.Key.Replace("*", "")))
                    {
                        HttpResponseMessage httpResponse = new HttpResponseMessage(response.Value.StatusCode)
                        {
                            Content = new StringContent(response.Value.Content, Encoding.UTF8, "application/json")
                        };
                        return Task.FromResult(httpResponse);
                    }
                }

                // Default 404 response
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }
    }
} 