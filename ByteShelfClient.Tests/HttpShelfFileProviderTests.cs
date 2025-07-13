using ByteShelfCommon;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Text;
using System.Text.Json;

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
            _provider = new HttpShelfFileProvider(_httpClient, "test-api-key");
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
            _messageHandler.SetupResponse($"api/files/{fileId}/download", "Hello World!");

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
        public async Task ReadFileAsync_WithChunkedOption_ReturnsShelfFileWithMetadata()
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
            ShelfFile result = await _provider.ReadFileAsync(fileId, useChunked: true);

            // Assert
            Assert.AreEqual(metadata.Id, result.Metadata.Id);
            Assert.AreEqual(metadata.OriginalFilename, result.Metadata.OriginalFilename);
            Assert.IsNotNull(result.GetContentStream());
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

        [TestMethod]
        public async Task GetTenantInfo_WithValidResponse_ReturnsTenantInfo()
        {
            // Arrange
            TenantInfoResponse expectedTenantInfo = new TenantInfoResponse(
                "test-tenant",
                "Test Tenant",
                false,
                1024 * 1024 * 100,
                1024 * 1024 * 25,
                1024 * 1024 * 75,
                25.0
            );

            string jsonResponse = JsonSerializer.Serialize(expectedTenantInfo);
            _messageHandler.SetupResponse("api/tenant/info", jsonResponse);

            // Act
            TenantInfoResponse result = await _provider.GetTenantInfoAsync();

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(expectedTenantInfo.TenantId, result.TenantId);
            Assert.AreEqual(expectedTenantInfo.DisplayName, result.DisplayName);
            Assert.AreEqual(expectedTenantInfo.StorageLimitBytes, result.StorageLimitBytes);
            Assert.AreEqual(expectedTenantInfo.IsAdmin, result.IsAdmin);
        }

        [TestMethod]
        public async Task GetSubTenants_WithValidResponse_ReturnsSubTenants()
        {
            // Arrange
            Dictionary<string, TenantInfo> expectedSubTenants = new Dictionary<string, TenantInfo>
            {
                ["subtenant1"] = new TenantInfo
                {
                    ApiKey = "sub-key-1",
                    DisplayName = "Sub Tenant 1",
                    StorageLimitBytes = 1024 * 1024 * 50,
                    IsAdmin = false
                },
                ["subtenant2"] = new TenantInfo
                {
                    ApiKey = "sub-key-2",
                    DisplayName = "Sub Tenant 2",
                    StorageLimitBytes = 1024 * 1024 * 100,
                    IsAdmin = false
                }
            };

            string jsonResponse = JsonSerializer.Serialize(expectedSubTenants);
            _messageHandler.SetupResponse("api/tenant/subtenants", jsonResponse);

            // Act
            Dictionary<string, TenantInfo> result = await _provider.GetSubTenantsAsync();

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.ContainsKey("subtenant1"));
            Assert.IsTrue(result.ContainsKey("subtenant2"));
            Assert.AreEqual("Sub Tenant 1", result["subtenant1"].DisplayName);
            Assert.AreEqual("Sub Tenant 2", result["subtenant2"].DisplayName);
        }

        [TestMethod]
        public async Task GetSubTenants_WithEmptyResponse_ReturnsEmptyDictionary()
        {
            // Arrange
            Dictionary<string, TenantInfo> emptySubTenants = new Dictionary<string, TenantInfo>();
            string jsonResponse = JsonSerializer.Serialize(emptySubTenants);
            _messageHandler.SetupResponse("api/tenant/subtenants", jsonResponse);

            // Act
            Dictionary<string, TenantInfo> result = await _provider.GetSubTenantsAsync();

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public async Task GetSubTenants_WithHttpError_ThrowsHttpRequestException()
        {
            // Arrange
            _messageHandler.SetupResponse("api/tenant/subtenants", "Server Error", HttpStatusCode.InternalServerError);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
                _provider.GetSubTenantsAsync());
        }

        [TestMethod]
        public async Task GetSubTenant_WithValidResponse_ReturnsSubTenant()
        {
            // Arrange
            string subTenantId = "subtenant1";
            TenantInfo expectedSubTenant = new TenantInfo
            {
                ApiKey = "sub-key",
                DisplayName = "Sub Tenant",
                StorageLimitBytes = 1024 * 1024 * 50,
                IsAdmin = false
            };

            string jsonResponse = JsonSerializer.Serialize(expectedSubTenant);
            _messageHandler.SetupResponse($"api/tenant/subtenants/{subTenantId}", jsonResponse);

            // Act
            TenantInfo result = await _provider.GetSubTenantAsync(subTenantId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(expectedSubTenant.DisplayName, result.DisplayName);
            Assert.AreEqual(expectedSubTenant.ApiKey, result.ApiKey);
            Assert.AreEqual(expectedSubTenant.StorageLimitBytes, result.StorageLimitBytes);
            Assert.AreEqual(expectedSubTenant.IsAdmin, result.IsAdmin);
            Assert.AreEqual(expectedSubTenant.Parent, result.Parent);
        }

        [TestMethod]
        public async Task GetSubTenant_WithNullSubTenantId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.GetSubTenantAsync(null!));
        }

        [TestMethod]
        public async Task GetSubTenant_WithEmptySubTenantId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.GetSubTenantAsync(""));
        }

        [TestMethod]
        public async Task GetSubTenant_WithWhitespaceSubTenantId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.GetSubTenantAsync("   "));
        }

        [TestMethod]
        public async Task GetSubTenant_WithNotFoundResponse_ThrowsFileNotFoundException()
        {
            // Arrange
            string subTenantId = "nonexistent";
            _messageHandler.SetupResponse($"api/tenant/subtenants/{subTenantId}", "Subtenant not found", HttpStatusCode.NotFound);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(() =>
                _provider.GetSubTenantAsync(subTenantId));
        }

        [TestMethod]
        public async Task GetSubTenant_WithHttpError_ThrowsHttpRequestException()
        {
            // Arrange
            string subTenantId = "subtenant1";
            _messageHandler.SetupResponse($"api/tenant/subtenants/{subTenantId}", "Server Error", HttpStatusCode.InternalServerError);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
                _provider.GetSubTenantAsync(subTenantId));
        }

        [TestMethod]
        public async Task GetSubTenantsUnderSubTenant_WithValidResponse_ReturnsSubTenants()
        {
            // Arrange
            string parentSubtenantId = "parent-subtenant1";
            Dictionary<string, TenantInfo> expectedSubTenants = new Dictionary<string, TenantInfo>
            {
                ["subtenant1"] = new TenantInfo
                {
                    ApiKey = "sub-key-1",
                    DisplayName = "Sub Tenant 1",
                    StorageLimitBytes = 1024 * 1024 * 50,
                    IsAdmin = false
                },
                ["subtenant2"] = new TenantInfo
                {
                    ApiKey = "sub-key-2",
                    DisplayName = "Sub Tenant 2",
                    StorageLimitBytes = 1024 * 1024 * 100,
                    IsAdmin = false
                }
            };

            string jsonResponse = JsonSerializer.Serialize(expectedSubTenants);
            _messageHandler.SetupResponse($"api/tenant/subtenants/{parentSubtenantId}/subtenants", jsonResponse);

            // Act
            Dictionary<string, TenantInfo> result = await _provider.GetSubTenantsUnderSubTenantAsync(parentSubtenantId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Count);
            Assert.IsTrue(result.ContainsKey("subtenant1"));
            Assert.IsTrue(result.ContainsKey("subtenant2"));
            Assert.AreEqual("Sub Tenant 1", result["subtenant1"].DisplayName);
            Assert.AreEqual("Sub Tenant 2", result["subtenant2"].DisplayName);
        }

        [TestMethod]
        public async Task GetSubTenantsUnderSubTenant_WithEmptyResponse_ReturnsEmptyDictionary()
        {
            // Arrange
            string parentSubtenantId = "parent-subtenant1";
            Dictionary<string, TenantInfo> emptySubTenants = new Dictionary<string, TenantInfo>();
            string jsonResponse = JsonSerializer.Serialize(emptySubTenants);
            _messageHandler.SetupResponse($"api/tenant/subtenants/{parentSubtenantId}/subtenants", jsonResponse);

            // Act
            Dictionary<string, TenantInfo> result = await _provider.GetSubTenantsUnderSubTenantAsync(parentSubtenantId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public async Task GetSubTenantsUnderSubTenant_WithNullParentSubtenantId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.GetSubTenantsUnderSubTenantAsync(null!));
        }

        [TestMethod]
        public async Task GetSubTenantsUnderSubTenant_WithEmptyParentSubtenantId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.GetSubTenantsUnderSubTenantAsync(""));
        }

        [TestMethod]
        public async Task GetSubTenantsUnderSubTenant_WithWhitespaceParentSubtenantId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.GetSubTenantsUnderSubTenantAsync("   "));
        }

        [TestMethod]
        public async Task GetSubTenantsUnderSubTenant_WithNotFoundResponse_ThrowsFileNotFoundException()
        {
            // Arrange
            string parentSubtenantId = "nonexistent-parent";
            _messageHandler.SetupResponse($"api/tenant/subtenants/{parentSubtenantId}/subtenants", "Parent subtenant not found", HttpStatusCode.NotFound);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(() =>
                _provider.GetSubTenantsUnderSubTenantAsync(parentSubtenantId));
        }

        [TestMethod]
        public async Task GetSubTenantsUnderSubTenant_WithHttpError_ThrowsHttpRequestException()
        {
            // Arrange
            string parentSubtenantId = "parent-subtenant1";
            _messageHandler.SetupResponse($"api/tenant/subtenants/{parentSubtenantId}/subtenants", "Server Error", HttpStatusCode.InternalServerError);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
                _provider.GetSubTenantsUnderSubTenantAsync(parentSubtenantId));
        }

        [TestMethod]
        public async Task CreateSubTenant_WithValidResponse_ReturnsSubTenantId()
        {
            // Arrange
            string displayName = "New Subtenant";
            string expectedSubTenantId = "new-subtenant-id";

            CreateSubTenantResponse expectedResponse = new CreateSubTenantResponse(expectedSubTenantId, displayName, "Subtenant created successfully");

            string jsonResponse = JsonSerializer.Serialize(expectedResponse);
            _messageHandler.SetupResponse("api/tenant/subtenants", jsonResponse, HttpStatusCode.Created);

            // Act
            string result = await _provider.CreateSubTenantAsync(displayName);

            // Assert
            Assert.AreEqual(expectedSubTenantId, result);
        }

        [TestMethod]
        public async Task CreateSubTenant_WithNullDisplayName_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.CreateSubTenantAsync(null!));
        }

        [TestMethod]
        public async Task CreateSubTenant_WithEmptyDisplayName_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.CreateSubTenantAsync(""));
        }

        [TestMethod]
        public async Task CreateSubTenant_WithWhitespaceDisplayName_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.CreateSubTenantAsync("   "));
        }

        [TestMethod]
        public async Task CreateSubTenant_WithBadRequestResponse_ThrowsInvalidOperationException()
        {
            // Arrange
            string displayName = "New Subtenant";
            string errorMessage = "Cannot create subtenant: maximum depth reached";
            _messageHandler.SetupResponse("api/tenant/subtenants", errorMessage, HttpStatusCode.BadRequest);

            // Act & Assert
            InvalidOperationException exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                _provider.CreateSubTenantAsync(displayName));
            Assert.AreEqual($"Cannot create subtenant: {errorMessage}", exception.Message);
        }

        [TestMethod]
        public async Task CreateSubTenant_WithHttpError_ThrowsHttpRequestException()
        {
            // Arrange
            string displayName = "New Subtenant";
            _messageHandler.SetupResponse("api/tenant/subtenants", "Server Error", HttpStatusCode.InternalServerError);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
                _provider.CreateSubTenantAsync(displayName));
        }

        [TestMethod]
        public async Task CreateSubTenant_WithInvalidResponse_ThrowsHttpRequestException()
        {
            // Arrange
            string displayName = "New Subtenant";
            _messageHandler.SetupResponse("api/tenant/subtenants", "invalid json", HttpStatusCode.Created);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
                _provider.CreateSubTenantAsync(displayName));
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_WithValidResponse_ReturnsSubTenantId()
        {
            // Arrange
            string parentSubtenantId = "parent-subtenant";
            string displayName = "New Subtenant";
            string expectedSubTenantId = "new-subtenant-id";

            CreateSubTenantResponse expectedResponse = new CreateSubTenantResponse(expectedSubTenantId, displayName, "Subtenant created successfully");

            string jsonResponse = JsonSerializer.Serialize(expectedResponse);
            _messageHandler.SetupResponse($"api/tenant/subtenants/{parentSubtenantId}/subtenants", jsonResponse, HttpStatusCode.Created);

            // Act
            string result = await _provider.CreateSubTenantUnderSubTenantAsync(parentSubtenantId, displayName);

            // Assert
            Assert.AreEqual(expectedSubTenantId, result);
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_WithNullParentSubtenantId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.CreateSubTenantUnderSubTenantAsync(null!, "New Subtenant"));
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_WithEmptyParentSubtenantId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.CreateSubTenantUnderSubTenantAsync("", "New Subtenant"));
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_WithWhitespaceParentSubtenantId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.CreateSubTenantUnderSubTenantAsync("   ", "New Subtenant"));
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_WithNullDisplayName_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.CreateSubTenantUnderSubTenantAsync("parent-subtenant", null!));
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_WithEmptyDisplayName_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.CreateSubTenantUnderSubTenantAsync("parent-subtenant", ""));
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_WithWhitespaceDisplayName_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.CreateSubTenantUnderSubTenantAsync("parent-subtenant", "   "));
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_WithBadRequestResponse_ThrowsInvalidOperationException()
        {
            // Arrange
            string parentSubtenantId = "parent-subtenant";
            string displayName = "New Subtenant";
            string errorMessage = "Cannot create subtenant: maximum depth reached";
            _messageHandler.SetupResponse($"api/tenant/subtenants/{parentSubtenantId}/subtenants", errorMessage, HttpStatusCode.BadRequest);

            // Act & Assert
            InvalidOperationException exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                _provider.CreateSubTenantUnderSubTenantAsync(parentSubtenantId, displayName));
            Assert.AreEqual($"Cannot create subtenant: {errorMessage}", exception.Message);
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_WithNotFoundResponse_ThrowsFileNotFoundException()
        {
            // Arrange
            string parentSubtenantId = "nonexistent";
            string displayName = "New Subtenant";
            string errorMessage = "Parent subtenant with ID nonexistent not found";
            _messageHandler.SetupResponse($"api/tenant/subtenants/{parentSubtenantId}/subtenants", errorMessage, HttpStatusCode.NotFound);

            // Act & Assert
            FileNotFoundException exception = await Assert.ThrowsExceptionAsync<FileNotFoundException>(() =>
                _provider.CreateSubTenantUnderSubTenantAsync(parentSubtenantId, displayName));
            Assert.AreEqual($"Parent subtenant with ID {parentSubtenantId} not found", exception.Message);
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_WithHttpError_ThrowsHttpRequestException()
        {
            // Arrange
            string parentSubtenantId = "parent-subtenant";
            string displayName = "New Subtenant";
            _messageHandler.SetupResponse($"api/tenant/subtenants/{parentSubtenantId}/subtenants", "Server Error", HttpStatusCode.InternalServerError);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
                _provider.CreateSubTenantUnderSubTenantAsync(parentSubtenantId, displayName));
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_WithInvalidResponse_ThrowsHttpRequestException()
        {
            // Arrange
            string parentSubtenantId = "parent-subtenant";
            string displayName = "New Subtenant";
            _messageHandler.SetupResponse($"api/tenant/subtenants/{parentSubtenantId}/subtenants", "invalid json", HttpStatusCode.Created);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
                _provider.CreateSubTenantUnderSubTenantAsync(parentSubtenantId, displayName));
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_SendsCorrectRequest()
        {
            // Arrange
            string parentSubtenantId = "parent-subtenant";
            string displayName = "New Subtenant";
            CreateSubTenantResponse expectedResponse = new CreateSubTenantResponse("new-subtenant-id", displayName, "Subtenant created successfully");
            string jsonResponse = JsonSerializer.Serialize(expectedResponse);
            _messageHandler.SetupResponse($"api/tenant/subtenants/{parentSubtenantId}/subtenants", jsonResponse, HttpStatusCode.Created);

            // Act
            await _provider.CreateSubTenantUnderSubTenantAsync(parentSubtenantId, displayName);

            // Assert
            Assert.AreEqual(1, _messageHandler.Requests.Count);
            Assert.AreEqual(
                $"api/tenant/subtenants/{parentSubtenantId}/subtenants",
                _messageHandler.Requests[0].RequestUri!.PathAndQuery.TrimStart('/'),
                "Request path should match expected endpoint (ignoring leading slash)"
            );
            Assert.AreEqual(HttpMethod.Post, _messageHandler.Requests[0].Method);
        }

        [TestMethod]
        public async Task UpdateSubTenantStorageLimit_WithValidResponse_CompletesSuccessfully()
        {
            // Arrange
            string subTenantId = "subtenant1";
            long storageLimitBytes = 1024 * 1024 * 200; // 200MB

            _messageHandler.SetupResponse($"api/tenant/subtenants/{subTenantId}/storage-limit", "");

            // Act & Assert - Should not throw
            await _provider.UpdateSubTenantStorageLimitAsync(subTenantId, storageLimitBytes);
        }

        [TestMethod]
        public async Task UpdateSubTenantStorageLimit_WithNullSubTenantId_ThrowsArgumentException()
        {
            // Arrange
            long storageLimitBytes = 1024 * 1024 * 200;

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.UpdateSubTenantStorageLimitAsync(null!, storageLimitBytes));
        }

        [TestMethod]
        public async Task UpdateSubTenantStorageLimit_WithEmptySubTenantId_ThrowsArgumentException()
        {
            // Arrange
            long storageLimitBytes = 1024 * 1024 * 200;

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.UpdateSubTenantStorageLimitAsync("", storageLimitBytes));
        }

        [TestMethod]
        public async Task UpdateSubTenantStorageLimit_WithWhitespaceSubTenantId_ThrowsArgumentException()
        {
            // Arrange
            long storageLimitBytes = 1024 * 1024 * 200;

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.UpdateSubTenantStorageLimitAsync("   ", storageLimitBytes));
        }

        [TestMethod]
        public async Task UpdateSubTenantStorageLimit_WithNegativeStorageLimit_ThrowsArgumentException()
        {
            // Arrange
            string subTenantId = "subtenant1";
            long negativeStorageLimit = -1024;

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.UpdateSubTenantStorageLimitAsync(subTenantId, negativeStorageLimit));
        }

        [TestMethod]
        public async Task UpdateSubTenantStorageLimit_WithNotFoundResponse_ThrowsFileNotFoundException()
        {
            // Arrange
            string subTenantId = "nonexistent";
            long storageLimitBytes = 1024 * 1024 * 200;
            _messageHandler.SetupResponse($"api/tenant/subtenants/{subTenantId}/storage-limit", "Subtenant not found", HttpStatusCode.NotFound);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(() =>
                _provider.UpdateSubTenantStorageLimitAsync(subTenantId, storageLimitBytes));
        }

        [TestMethod]
        public async Task UpdateSubTenantStorageLimit_WithHttpError_ThrowsHttpRequestException()
        {
            // Arrange
            string subTenantId = "subtenant1";
            long storageLimitBytes = 1024 * 1024 * 200;
            _messageHandler.SetupResponse($"api/tenant/subtenants/{subTenantId}/storage-limit", "Server Error", HttpStatusCode.InternalServerError);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
                _provider.UpdateSubTenantStorageLimitAsync(subTenantId, storageLimitBytes));
        }

        [TestMethod]
        public async Task DeleteSubTenant_WithValidResponse_CompletesSuccessfully()
        {
            // Arrange
            string subTenantId = "subtenant1";
            _messageHandler.SetupResponse($"api/tenant/subtenants/{subTenantId}", "");

            // Act & Assert - Should not throw
            await _provider.DeleteSubTenantAsync(subTenantId);
        }

        [TestMethod]
        public async Task DeleteSubTenant_WithNullSubTenantId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.DeleteSubTenantAsync(null!));
        }

        [TestMethod]
        public async Task DeleteSubTenant_WithEmptySubTenantId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.DeleteSubTenantAsync(""));
        }

        [TestMethod]
        public async Task DeleteSubTenant_WithWhitespaceSubTenantId_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _provider.DeleteSubTenantAsync("   "));
        }

        [TestMethod]
        public async Task DeleteSubTenant_WithNotFoundResponse_ThrowsFileNotFoundException()
        {
            // Arrange
            string subTenantId = "nonexistent";
            _messageHandler.SetupResponse($"api/tenant/subtenants/{subTenantId}", "Subtenant not found", HttpStatusCode.NotFound);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(() =>
                _provider.DeleteSubTenantAsync(subTenantId));
        }

        [TestMethod]
        public async Task DeleteSubTenant_WithHttpError_ThrowsHttpRequestException()
        {
            // Arrange
            string subTenantId = "subtenant1";
            _messageHandler.SetupResponse($"api/tenant/subtenants/{subTenantId}", "Server Error", HttpStatusCode.InternalServerError);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() =>
                _provider.DeleteSubTenantAsync(subTenantId));
        }

        [TestMethod]
        public async Task GetSubTenants_SendsCorrectRequest()
        {
            // Arrange
            Dictionary<string, TenantInfo> expectedSubTenants = new Dictionary<string, TenantInfo>();
            string jsonResponse = JsonSerializer.Serialize(expectedSubTenants);
            _messageHandler.SetupResponse("api/tenant/subtenants", jsonResponse);

            // Act
            await _provider.GetSubTenantsAsync();

            // Assert
            Assert.AreEqual(1, _messageHandler.Requests.Count);
            Assert.AreEqual(
                "api/tenant/subtenants",
                _messageHandler.Requests[0].RequestUri!.PathAndQuery.TrimStart('/'),
                "Request path should match expected endpoint (ignoring leading slash)"
            );
        }

        [TestMethod]
        public async Task GetSubTenant_SendsCorrectRequest()
        {
            // Arrange
            string subTenantId = "subtenant1";
            TenantInfo expectedSubTenant = new TenantInfo();
            string jsonResponse = JsonSerializer.Serialize(expectedSubTenant);
            _messageHandler.SetupResponse($"api/tenant/subtenants/{subTenantId}", jsonResponse);

            // Act
            await _provider.GetSubTenantAsync(subTenantId);

            // Assert
            Assert.AreEqual(1, _messageHandler.Requests.Count);
            Assert.AreEqual(
                $"api/tenant/subtenants/{subTenantId}",
                _messageHandler.Requests[0].RequestUri!.PathAndQuery.TrimStart('/'),
                "Request path should match expected endpoint (ignoring leading slash)"
            );
        }

        [TestMethod]
        public async Task GetSubTenantsUnderSubTenant_SendsCorrectRequest()
        {
            // Arrange
            string parentSubtenantId = "parent-subtenant1";
            Dictionary<string, TenantInfo> expectedSubTenants = new Dictionary<string, TenantInfo>();
            string jsonResponse = JsonSerializer.Serialize(expectedSubTenants);
            _messageHandler.SetupResponse($"api/tenant/subtenants/{parentSubtenantId}/subtenants", jsonResponse);

            // Act
            await _provider.GetSubTenantsUnderSubTenantAsync(parentSubtenantId);

            // Assert
            Assert.AreEqual(1, _messageHandler.Requests.Count);
            Assert.AreEqual(
                $"api/tenant/subtenants/{parentSubtenantId}/subtenants",
                _messageHandler.Requests[0].RequestUri!.PathAndQuery.TrimStart('/'),
                "Request path should match expected endpoint (ignoring leading slash)"
            );
        }

        [TestMethod]
        public async Task CreateSubTenant_SendsCorrectRequest()
        {
            // Arrange
            string displayName = "New Subtenant";
            CreateSubTenantResponse expectedResponse = new CreateSubTenantResponse("new-subtenant-id", displayName, "Subtenant created successfully");
            string jsonResponse = JsonSerializer.Serialize(expectedResponse);
            _messageHandler.SetupResponse("api/tenant/subtenants", jsonResponse, HttpStatusCode.Created);

            // Act
            await _provider.CreateSubTenantAsync(displayName);

            // Assert
            Assert.AreEqual(1, _messageHandler.Requests.Count);
            Assert.AreEqual(
                "api/tenant/subtenants",
                _messageHandler.Requests[0].RequestUri!.PathAndQuery.TrimStart('/'),
                "Request path should match expected endpoint (ignoring leading slash)"
            );
            Assert.AreEqual(HttpMethod.Post, _messageHandler.Requests[0].Method);
        }

        [TestMethod]
        public async Task UpdateSubTenantStorageLimit_SendsCorrectRequest()
        {
            // Arrange
            string subTenantId = "subtenant1";
            long storageLimitBytes = 1024 * 1024 * 200;
            _messageHandler.SetupResponse($"api/tenant/subtenants/{subTenantId}/storage-limit", "");

            // Act
            await _provider.UpdateSubTenantStorageLimitAsync(subTenantId, storageLimitBytes);

            // Assert
            Assert.AreEqual(1, _messageHandler.Requests.Count);
            Assert.AreEqual(
                $"api/tenant/subtenants/{subTenantId}/storage-limit",
                _messageHandler.Requests[0].RequestUri!.PathAndQuery.TrimStart('/'),
                "Request path should match expected endpoint (ignoring leading slash)"
            );
            Assert.AreEqual(HttpMethod.Put, _messageHandler.Requests[0].Method);
        }

        [TestMethod]
        public async Task DeleteSubTenant_SendsCorrectRequest()
        {
            // Arrange
            string subTenantId = "subtenant1";
            _messageHandler.SetupResponse($"api/tenant/subtenants/{subTenantId}", "");

            // Act
            await _provider.DeleteSubTenantAsync(subTenantId);

            // Assert
            Assert.AreEqual(1, _messageHandler.Requests.Count);
            Assert.AreEqual(
                $"api/tenant/subtenants/{subTenantId}",
                _messageHandler.Requests[0].RequestUri!.PathAndQuery.TrimStart('/'),
                "Request path should match expected endpoint (ignoring leading slash)"
            );
            Assert.AreEqual(HttpMethod.Delete, _messageHandler.Requests[0].Method);
        }
    }
}