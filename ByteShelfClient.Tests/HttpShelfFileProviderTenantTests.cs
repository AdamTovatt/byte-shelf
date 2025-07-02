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
            _provider = new HttpShelfFileProvider(_httpClient);
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

            HttpShelfFileProvider.TenantStorageInfo expectedResponse = new HttpShelfFileProvider.TenantStorageInfo
            {
                TenantId = "tenant1",
                CurrentUsageBytes = 1024 * 1024 * 25, // 25MB
                StorageLimitBytes = 1024 * 1024 * 100, // 100MB
                AvailableSpaceBytes = 1024 * 1024 * 75, // 75MB
                UsagePercentage = 25.0
            };

            string jsonResponse = JsonSerializer.Serialize(expectedResponse);
            _messageHandler.SetupResponse("api/tenant/storage", jsonResponse);

            // Act
            HttpShelfFileProvider.TenantStorageInfo result = await providerWithApiKey.GetStorageInfoAsync();

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(expectedResponse.TenantId, result.TenantId);
            Assert.AreEqual(expectedResponse.CurrentUsageBytes, result.CurrentUsageBytes);
            Assert.AreEqual(expectedResponse.StorageLimitBytes, result.StorageLimitBytes);
            Assert.AreEqual(expectedResponse.AvailableSpaceBytes, result.AvailableSpaceBytes);
            Assert.AreEqual(expectedResponse.UsagePercentage, result.UsagePercentage);
        }

        [TestMethod]
        public async Task GetStorageInfoAsync_WithoutApiKey_ThrowsInvalidOperationException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _provider.GetStorageInfoAsync());
        }

        [TestMethod]
        public async Task GetStorageInfoAsync_WithEmptyApiKey_ThrowsInvalidOperationException()
        {
            // Arrange
            HttpShelfFileProvider providerWithEmptyApiKey = new HttpShelfFileProvider(_httpClient, "");

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => providerWithEmptyApiKey.GetStorageInfoAsync());
        }

        [TestMethod]
        public async Task GetStorageInfoAsync_WithWhitespaceApiKey_ThrowsInvalidOperationException()
        {
            // Arrange
            HttpShelfFileProvider providerWithWhitespaceApiKey = new HttpShelfFileProvider(_httpClient, "   ");

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => providerWithWhitespaceApiKey.GetStorageInfoAsync());
        }

        [TestMethod]
        public async Task GetStorageInfoAsync_WithUnlimitedStorage_ReturnsCorrectValues()
        {
            // Arrange
            HttpShelfFileProvider providerWithApiKey = new HttpShelfFileProvider(_httpClient, "admin-key");

            HttpShelfFileProvider.TenantStorageInfo expectedResponse = new HttpShelfFileProvider.TenantStorageInfo
            {
                TenantId = "admin",
                CurrentUsageBytes = 1024 * 1024 * 5, // 5MB
                StorageLimitBytes = 0, // Unlimited
                AvailableSpaceBytes = 0, // 0 for unlimited
                UsagePercentage = 0.0 // 0% for unlimited
            };

            string jsonResponse = JsonSerializer.Serialize(expectedResponse);
            _messageHandler.SetupResponse("api/tenant/storage", jsonResponse);

            // Act
            HttpShelfFileProvider.TenantStorageInfo result = await providerWithApiKey.GetStorageInfoAsync();

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

            HttpShelfFileProvider.QuotaCheckResult expectedResponse = new HttpShelfFileProvider.QuotaCheckResult
            {
                TenantId = "tenant1",
                FileSizeBytes = fileSize,
                CanStore = true,
                CurrentUsageBytes = 1024 * 1024 * 20, // 20MB
                StorageLimitBytes = 1024 * 1024 * 100, // 100MB
                AvailableSpaceBytes = 1024 * 1024 * 80, // 80MB
                WouldExceedQuota = false
            };

            string jsonResponse = JsonSerializer.Serialize(expectedResponse);
            _messageHandler.SetupResponse($"api/tenant/storage/can-store?fileSizeBytes={fileSize}", jsonResponse);

            // Act
            HttpShelfFileProvider.QuotaCheckResult result = await providerWithApiKey.CanStoreFileAsync(fileSize);

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

            HttpShelfFileProvider.QuotaCheckResult expectedResponse = new HttpShelfFileProvider.QuotaCheckResult
            {
                TenantId = "tenant1",
                FileSizeBytes = fileSize,
                CanStore = false,
                CurrentUsageBytes = 1024 * 1024 * 20, // 20MB
                StorageLimitBytes = 1024 * 1024 * 100, // 100MB
                AvailableSpaceBytes = 1024 * 1024 * 80, // 80MB
                WouldExceedQuota = true
            };

            string jsonResponse = JsonSerializer.Serialize(expectedResponse);
            _messageHandler.SetupResponse($"api/tenant/storage/can-store?fileSizeBytes={fileSize}", jsonResponse);

            // Act
            HttpShelfFileProvider.QuotaCheckResult result = await providerWithApiKey.CanStoreFileAsync(fileSize);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(expectedResponse.TenantId, result.TenantId);
            Assert.AreEqual(expectedResponse.FileSizeBytes, result.FileSizeBytes);
            Assert.AreEqual(expectedResponse.CanStore, result.CanStore);
            Assert.AreEqual(expectedResponse.WouldExceedQuota, result.WouldExceedQuota);
        }

        [TestMethod]
        public async Task CanStoreFileAsync_WithoutApiKey_ThrowsInvalidOperationException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _provider.CanStoreFileAsync(1024));
        }

        [TestMethod]
        public async Task CanStoreFileAsync_WithZeroFileSize_ReturnsTrue()
        {
            // Arrange
            HttpShelfFileProvider providerWithApiKey = new HttpShelfFileProvider(_httpClient, "tenant1-key");
            long fileSize = 0;

            HttpShelfFileProvider.QuotaCheckResult expectedResponse = new HttpShelfFileProvider.QuotaCheckResult
            {
                TenantId = "tenant1",
                FileSizeBytes = fileSize,
                CanStore = true,
                CurrentUsageBytes = 1024 * 1024 * 20,
                StorageLimitBytes = 1024 * 1024 * 100,
                AvailableSpaceBytes = 1024 * 1024 * 80,
                WouldExceedQuota = false
            };

            string jsonResponse = JsonSerializer.Serialize(expectedResponse);
            _messageHandler.SetupResponse($"api/tenant/storage/can-store?fileSizeBytes={fileSize}", jsonResponse);

            // Act
            HttpShelfFileProvider.QuotaCheckResult result = await providerWithApiKey.CanStoreFileAsync(fileSize);

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

            HttpShelfFileProvider.QuotaCheckResult expectedResponse = new HttpShelfFileProvider.QuotaCheckResult
            {
                TenantId = "tenant1",
                FileSizeBytes = fileSize,
                CanStore = true,
                CurrentUsageBytes = 1024 * 1024 * 20,
                StorageLimitBytes = 1024 * 1024 * 100,
                AvailableSpaceBytes = 1024 * 1024 * 80,
                WouldExceedQuota = false
            };

            string jsonResponse = JsonSerializer.Serialize(expectedResponse);
            _messageHandler.SetupResponse($"api/tenant/storage/can-store?fileSizeBytes={fileSize}", jsonResponse);

            // Act
            HttpShelfFileProvider.QuotaCheckResult result = await providerWithApiKey.CanStoreFileAsync(fileSize);

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
            HttpShelfFileProvider.QuotaCheckResult quotaResponse = new HttpShelfFileProvider.QuotaCheckResult
            {
                TenantId = "tenant1",
                FileSizeBytes = content.Length,
                CanStore = true,
                CurrentUsageBytes = 1024 * 1024 * 20,
                StorageLimitBytes = 1024 * 1024 * 100,
                AvailableSpaceBytes = 1024 * 1024 * 80,
                WouldExceedQuota = false
            };

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
            HttpShelfFileProvider.QuotaCheckResult quotaResponse = new HttpShelfFileProvider.QuotaCheckResult
            {
                TenantId = "tenant1",
                FileSizeBytes = content.Length,
                CanStore = false,
                CurrentUsageBytes = 1024 * 1024 * 95, // Almost full
                StorageLimitBytes = 1024 * 1024 * 100,
                AvailableSpaceBytes = 1024 * 1024 * 5,
                WouldExceedQuota = true
            };

            string quotaJsonResponse = JsonSerializer.Serialize(quotaResponse);
            _messageHandler.SetupResponse($"api/tenant/storage/can-store?fileSizeBytes={content.Length}", quotaJsonResponse);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => providerWithApiKey.WriteFileWithQuotaCheckAsync(filename, contentType, contentStream));
        }

        [TestMethod]
        public async Task WriteFileWithQuotaCheckAsync_WithoutApiKey_ThrowsInvalidOperationException()
        {
            // Arrange
            string content = "Test file content";
            string filename = "test.txt";
            string contentType = "text/plain";
            using MemoryStream contentStream = new MemoryStream(Encoding.UTF8.GetBytes(content));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _provider.WriteFileWithQuotaCheckAsync(filename, contentType, contentStream));
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
        public async Task GetStorageInfoAsync_WithCancellationToken_RespectsCancellation()
        {
            // Arrange
            HttpShelfFileProvider providerWithApiKey = new HttpShelfFileProvider(_httpClient, "tenant1-key");
            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Act & Assert
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                () => providerWithApiKey.GetStorageInfoAsync(cts.Token));
        }

        [TestMethod]
        public async Task CanStoreFileAsync_WithCancellationToken_RespectsCancellation()
        {
            // Arrange
            HttpShelfFileProvider providerWithApiKey = new HttpShelfFileProvider(_httpClient, "tenant1-key");
            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Act & Assert
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                () => providerWithApiKey.CanStoreFileAsync(1024, cts.Token));
        }

        [TestMethod]
        public async Task WriteFileWithQuotaCheckAsync_WithCancellationToken_RespectsCancellation()
        {
            // Arrange
            HttpShelfFileProvider providerWithApiKey = new HttpShelfFileProvider(_httpClient, "tenant1-key");
            using MemoryStream contentStream = new MemoryStream(Encoding.UTF8.GetBytes("test"));
            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Act & Assert
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                () => providerWithApiKey.WriteFileWithQuotaCheckAsync("test.txt", "text/plain", contentStream, cancellationToken: cts.Token));
        }
    }
}