using ByteShelfCommon;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ByteShelfClient.Tests
{
    [TestClass]
    public class HttpShelfFileProviderTenantTests
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
        public async Task GetStorageInfoAsync_WithApiKey_ReturnsStorageInfo()
        {
            // Arrange
            HttpShelfFileProvider providerWithApiKey = new HttpShelfFileProvider(_httpClient, "tenant1-key");

            TenantStorageInfo expectedResponse = new TenantStorageInfo(
                "tenant1",
                1024 * 1024 * 25, // 25MB
                1024 * 1024 * 100, // 100MB
                1024 * 1024 * 75, // 75MB
                25.0
            );

            string jsonResponse = JsonSerializer.Serialize(expectedResponse);
            _messageHandler.SetupResponse("api/tenant/storage", jsonResponse);

            // Act
            TenantStorageInfo result = await providerWithApiKey.GetStorageInfoAsync();

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(expectedResponse.TenantId, result.TenantId);
            Assert.AreEqual(expectedResponse.CurrentUsageBytes, result.CurrentUsageBytes);
            Assert.AreEqual(expectedResponse.StorageLimitBytes, result.StorageLimitBytes);
            Assert.AreEqual(expectedResponse.AvailableSpaceBytes, result.AvailableSpaceBytes);
            Assert.AreEqual(expectedResponse.UsagePercentage, result.UsagePercentage);
        }

        [TestMethod]
        public async Task GetStorageInfoAsync_WithUnlimitedStorage_ReturnsCorrectValues()
        {
            // Arrange
            HttpShelfFileProvider providerWithApiKey = new HttpShelfFileProvider(_httpClient, "admin-key");

            TenantStorageInfo expectedResponse = new TenantStorageInfo(
                "admin",
                1024 * 1024 * 5, // 5MB
                0, // Unlimited
                0, // 0 for unlimited
                0.0 // 0% for unlimited
            );

            string jsonResponse = JsonSerializer.Serialize(expectedResponse);
            _messageHandler.SetupResponse("api/tenant/storage", jsonResponse);

            // Act
            TenantStorageInfo result = await providerWithApiKey.GetStorageInfoAsync();

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(expectedResponse.TenantId, result.TenantId);
            Assert.AreEqual(expectedResponse.CurrentUsageBytes, result.CurrentUsageBytes);
            Assert.AreEqual(expectedResponse.StorageLimitBytes, result.StorageLimitBytes);
            Assert.AreEqual(expectedResponse.AvailableSpaceBytes, result.AvailableSpaceBytes);
            Assert.AreEqual(expectedResponse.UsagePercentage, result.UsagePercentage);
        }

        [TestMethod]
        public async Task CanStoreFileAsync_WithApiKeyAndCanStore_ReturnsTrue()
        {
            // Arrange
            HttpShelfFileProvider providerWithApiKey = new HttpShelfFileProvider(_httpClient, "tenant1-key");
            long fileSize = 1024 * 1024 * 10; // 10MB

            QuotaCheckResult expectedResponse = new QuotaCheckResult(
                "tenant1",
                fileSize,
                true,
                1024 * 1024 * 20, // 20MB
                1024 * 1024 * 100, // 100MB
                1024 * 1024 * 80, // 80MB
                false
            );

            string jsonResponse = JsonSerializer.Serialize(expectedResponse);
            _messageHandler.SetupResponse($"api/tenant/storage/can-store?fileSizeBytes={fileSize}", jsonResponse);

            // Act
            QuotaCheckResult result = await providerWithApiKey.CanStoreFileAsync(fileSize);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(expectedResponse.TenantId, result.TenantId);
            Assert.AreEqual(expectedResponse.FileSizeBytes, result.FileSizeBytes);
            Assert.AreEqual(expectedResponse.CanStore, result.CanStore);
            Assert.AreEqual(expectedResponse.WouldExceedQuota, result.WouldExceedQuota);
        }

        [TestMethod]
        public async Task CanStoreFileAsync_WithApiKeyAndCannotStore_ReturnsFalse()
        {
            // Arrange
            HttpShelfFileProvider providerWithApiKey = new HttpShelfFileProvider(_httpClient, "tenant1-key");
            long fileSize = 1024 * 1024 * 90; // 90MB

            QuotaCheckResult expectedResponse = new QuotaCheckResult(
                "tenant1",
                fileSize,
                false,
                1024 * 1024 * 20, // 20MB
                1024 * 1024 * 100, // 100MB
                1024 * 1024 * 80, // 80MB
                true
            );

            string jsonResponse = JsonSerializer.Serialize(expectedResponse);
            _messageHandler.SetupResponse($"api/tenant/storage/can-store?fileSizeBytes={fileSize}", jsonResponse);

            // Act
            QuotaCheckResult result = await providerWithApiKey.CanStoreFileAsync(fileSize);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(expectedResponse.TenantId, result.TenantId);
            Assert.AreEqual(expectedResponse.FileSizeBytes, result.FileSizeBytes);
            Assert.AreEqual(expectedResponse.CanStore, result.CanStore);
            Assert.AreEqual(expectedResponse.WouldExceedQuota, result.WouldExceedQuota);
        }

        [TestMethod]
        public async Task CanStoreFileAsync_WithZeroFileSize_ReturnsTrue()
        {
            // Arrange
            HttpShelfFileProvider providerWithApiKey = new HttpShelfFileProvider(_httpClient, "tenant1-key");
            long fileSize = 0;

            QuotaCheckResult expectedResponse = new QuotaCheckResult(
                "tenant1",
                fileSize,
                true,
                1024 * 1024 * 20,
                1024 * 1024 * 100,
                1024 * 1024 * 80,
                false
            );

            string jsonResponse = JsonSerializer.Serialize(expectedResponse);
            _messageHandler.SetupResponse($"api/tenant/storage/can-store?fileSizeBytes={fileSize}", jsonResponse);

            // Act
            QuotaCheckResult result = await providerWithApiKey.CanStoreFileAsync(fileSize);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(expectedResponse.CanStore, result.CanStore);
            Assert.AreEqual(expectedResponse.FileSizeBytes, result.FileSizeBytes);
        }

        [TestMethod]
        public async Task CanStoreFileAsync_WithNegativeFileSize_HandlesGracefully()
        {
            // Arrange
            HttpShelfFileProvider providerWithApiKey = new HttpShelfFileProvider(_httpClient, "tenant1-key");
            long fileSize = -1024;

            QuotaCheckResult expectedResponse = new QuotaCheckResult(
                "tenant1",
                fileSize,
                true,
                1024 * 1024 * 20,
                1024 * 1024 * 100,
                1024 * 1024 * 80,
                false
            );

            string jsonResponse = JsonSerializer.Serialize(expectedResponse);
            _messageHandler.SetupResponse($"api/tenant/storage/can-store?fileSizeBytes={fileSize}", jsonResponse);

            // Act
            QuotaCheckResult result = await providerWithApiKey.CanStoreFileAsync(fileSize);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(expectedResponse.FileSizeBytes, result.FileSizeBytes);
        }

        [TestMethod]
        public async Task WriteFileWithQuotaCheckAsync_WithApiKeyAndCanStore_WritesFile()
        {
            // Arrange
            HttpShelfFileProvider providerWithApiKey = new HttpShelfFileProvider(_httpClient, "tenant1-key");
            string content = "Test file content";
            string filename = "test.txt";
            string contentType = "text/plain";
            using MemoryStream contentStream = new MemoryStream(Encoding.UTF8.GetBytes(content));

            // Setup quota check response
            QuotaCheckResult quotaResponse = new QuotaCheckResult(
                "tenant1",
                content.Length,
                true,
                1024 * 1024 * 20,
                1024 * 1024 * 100,
                1024 * 1024 * 80,
                false
            );

            string quotaJsonResponse = JsonSerializer.Serialize(quotaResponse);
            _messageHandler.SetupResponse($"api/tenant/storage/can-store?fileSizeBytes={content.Length}", quotaJsonResponse);

            // Setup chunk size config
            object config = new { ChunkSizeBytes = 1024 };
            string configJson = JsonSerializer.Serialize(config);
            _messageHandler.SetupResponse("api/config/chunk-size", configJson);

            // Setup file upload responses
            _messageHandler.SetupResponse("api/chunks/*", "OK", HttpStatusCode.OK);
            _messageHandler.SetupResponse("api/files/metadata", "OK", HttpStatusCode.Created);

            // Act
            Guid fileId = await providerWithApiKey.WriteFileWithQuotaCheckAsync(filename, contentType, contentStream);

            // Assert
            Assert.AreNotEqual(Guid.Empty, fileId);
            Assert.IsTrue(_messageHandler.Requests.Count > 0);
        }

        [TestMethod]
        public async Task WriteFileWithQuotaCheckAsync_WithApiKeyAndCannotStore_ThrowsInvalidOperationException()
        {
            // Arrange
            HttpShelfFileProvider providerWithApiKey = new HttpShelfFileProvider(_httpClient, "tenant1-key");
            string content = "Test file content";
            string filename = "test.txt";
            string contentType = "text/plain";
            using MemoryStream contentStream = new MemoryStream(Encoding.UTF8.GetBytes(content));

            // Setup quota check response indicating cannot store
            QuotaCheckResult quotaResponse = new QuotaCheckResult(
                "tenant1",
                content.Length,
                false,
                1024 * 1024 * 95, // Almost full
                1024 * 1024 * 100,
                1024 * 1024 * 5,
                true
            );

            string quotaJsonResponse = JsonSerializer.Serialize(quotaResponse);
            _messageHandler.SetupResponse($"api/tenant/storage/can-store?fileSizeBytes={content.Length}", quotaJsonResponse);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => providerWithApiKey.WriteFileWithQuotaCheckAsync(filename, contentType, contentStream));
        }

        [TestMethod]
        public async Task WriteFileWithQuotaCheckAsync_WithNullStream_ThrowsArgumentNullException()
        {
            // Arrange
            HttpShelfFileProvider providerWithApiKey = new HttpShelfFileProvider(_httpClient, "tenant1-key");

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => providerWithApiKey.WriteFileWithQuotaCheckAsync("test.txt", "text/plain", null!));
        }

        [TestMethod]
        public async Task WriteFileWithQuotaCheckAsync_WithEmptyFilename_ThrowsArgumentException()
        {
            // Arrange
            HttpShelfFileProvider providerWithApiKey = new HttpShelfFileProvider(_httpClient, "tenant1-key");
            using MemoryStream contentStream = new MemoryStream(Encoding.UTF8.GetBytes("test"));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => providerWithApiKey.WriteFileWithQuotaCheckAsync("", "text/plain", contentStream));
        }

        [TestMethod]
        public async Task WriteFileWithQuotaCheckAsync_WithNullFilename_ThrowsArgumentNullException()
        {
            // Arrange
            HttpShelfFileProvider providerWithApiKey = new HttpShelfFileProvider(_httpClient, "tenant1-key");
            using MemoryStream contentStream = new MemoryStream(Encoding.UTF8.GetBytes("test"));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => providerWithApiKey.WriteFileWithQuotaCheckAsync(null!, "text/plain", contentStream));
        }

        [TestMethod]
        public async Task WriteFileWithQuotaCheckAsync_WithNullContentType_ThrowsArgumentNullException()
        {
            // Arrange
            HttpShelfFileProvider providerWithApiKey = new HttpShelfFileProvider(_httpClient, "tenant1-key");
            using MemoryStream contentStream = new MemoryStream(Encoding.UTF8.GetBytes("test"));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => providerWithApiKey.WriteFileWithQuotaCheckAsync("test.txt", null!, contentStream));
        }

        [TestMethod]
        public async Task GetTenantInfoAsync_WithApiKey_ReturnsTenantInfo()
        {
            // Arrange
            HttpShelfFileProvider providerWithApiKey = new HttpShelfFileProvider(_httpClient, "tenant1-key");

            TenantInfoResponse expectedResponse = new TenantInfoResponse(
                "tenant1",
                "Test Tenant 1",
                false, // Not admin
                1024 * 1024 * 100, // 100MB limit
                1024 * 1024 * 25, // 25MB usage
                1024 * 1024 * 75, // 75MB available
                25.0 // 25% usage
            );

            string jsonResponse = JsonSerializer.Serialize(expectedResponse);
            _messageHandler.SetupResponse("api/tenant/info", jsonResponse);

            // Act
            TenantInfoResponse result = await providerWithApiKey.GetTenantInfoAsync();

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(expectedResponse.TenantId, result.TenantId);
            Assert.AreEqual(expectedResponse.DisplayName, result.DisplayName);
            Assert.AreEqual(expectedResponse.IsAdmin, result.IsAdmin);
            Assert.AreEqual(expectedResponse.StorageLimitBytes, result.StorageLimitBytes);
            Assert.AreEqual(expectedResponse.CurrentUsageBytes, result.CurrentUsageBytes);
            Assert.AreEqual(expectedResponse.AvailableSpaceBytes, result.AvailableSpaceBytes);
            Assert.AreEqual(expectedResponse.UsagePercentage, result.UsagePercentage);
        }

        [TestMethod]
        public async Task GetTenantInfoAsync_WithAdminApiKey_ReturnsAdminStatus()
        {
            // Arrange
            HttpShelfFileProvider providerWithApiKey = new HttpShelfFileProvider(_httpClient, "admin-key");

            TenantInfoResponse expectedResponse = new TenantInfoResponse(
                "admin",
                "Admin User",
                true, // Is admin
                0, // Unlimited storage
                1024 * 1024 * 5, // 5MB usage
                0, // 0 available for unlimited
                0.0 // 0% usage for unlimited
            );

            string jsonResponse = JsonSerializer.Serialize(expectedResponse);
            _messageHandler.SetupResponse("api/tenant/info", jsonResponse);

            // Act
            TenantInfoResponse result = await providerWithApiKey.GetTenantInfoAsync();

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(expectedResponse.TenantId, result.TenantId);
            Assert.AreEqual(expectedResponse.DisplayName, result.DisplayName);
            Assert.IsTrue(result.IsAdmin);
            Assert.AreEqual(0, result.StorageLimitBytes); // Unlimited
            Assert.AreEqual(expectedResponse.CurrentUsageBytes, result.CurrentUsageBytes);
            Assert.AreEqual(0, result.AvailableSpaceBytes); // 0 for unlimited
            Assert.AreEqual(0.0, result.UsagePercentage); // 0% for unlimited
        }

        [TestMethod]
        public void Constructor_WithNullApiKey_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(
                () => new HttpShelfFileProvider(_httpClient, null!));
        }

        [TestMethod]
        public void Constructor_WithEmptyApiKey_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentException>(
                () => new HttpShelfFileProvider(_httpClient, ""));
        }

        [TestMethod]
        public void Constructor_WithWhitespaceApiKey_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentException>(
                () => new HttpShelfFileProvider(_httpClient, "   "));
        }
    }
}