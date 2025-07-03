using ByteShelf.Configuration;
using ByteShelf.Controllers;
using ByteShelf.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace ByteShelf.Tests
{
    [TestClass]
    public class TenantControllerTests
    {
        private TenantController _controller = null!;
        private Mock<ITenantStorageService> _mockStorageService = null!;
        private Mock<ITenantConfigurationService> _mockConfigService = null!;
        private Mock<HttpContext> _mockHttpContext = null!;
        private TenantConfiguration _tenantConfig = null!;

        [TestInitialize]
        public void Setup()
        {
            _mockStorageService = new Mock<ITenantStorageService>();
            _mockConfigService = new Mock<ITenantConfigurationService>();
            _mockHttpContext = new Mock<HttpContext>();

            // Setup the Items dictionary properly
            Dictionary<object, object?> items = new Dictionary<object, object?>();
            _mockHttpContext.Setup(c => c.Items).Returns(items);

            // Setup tenant configuration
            _tenantConfig = new TenantConfiguration
            {
                Tenants = new Dictionary<string, TenantInfo>
                {
                    ["tenant1"] = new TenantInfo
                    {
                        ApiKey = "key1",
                        DisplayName = "Test Tenant 1",
                        StorageLimitBytes = 1024 * 1024 * 100, // 100MB
                        IsAdmin = false
                    },
                    ["admin"] = new TenantInfo
                    {
                        ApiKey = "admin-key",
                        DisplayName = "Admin User",
                        StorageLimitBytes = 0, // Unlimited
                        IsAdmin = true
                    }
                }
            };

            _mockConfigService.Setup(c => c.GetConfiguration()).Returns(_tenantConfig);

            _controller = new TenantController(_mockStorageService.Object, _mockConfigService.Object);
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = _mockHttpContext.Object
            };
        }

        [TestMethod]
        public void Constructor_WithNullStorageService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new TenantController(null!, _mockConfigService.Object));
        }

        [TestMethod]
        public void Constructor_WithNullConfigService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new TenantController(_mockStorageService.Object, null!));
        }

        [TestMethod]
        public async Task GetTenantInfo_ReturnsTenantInformation()
        {
            // Arrange
            string tenantId = "tenant1";
            long currentUsage = 1024 * 1024 * 25; // 25MB
            long storageLimit = 1024 * 1024 * 100; // 100MB

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockStorageService.Setup(s => s.GetCurrentUsage(tenantId)).Returns(currentUsage);

            // Act
            IActionResult result = await _controller.GetTenantInfo(CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            OkObjectResult okResult = (OkObjectResult)result;
            Assert.IsNotNull(okResult.Value);

            // Verify the response contains expected data
            TenantInfoResponse response = (TenantInfoResponse)okResult.Value;

            Assert.AreEqual(tenantId, response.TenantId);
            Assert.AreEqual("Test Tenant 1", response.DisplayName);
            Assert.IsFalse(response.IsAdmin);
            Assert.AreEqual(storageLimit, response.StorageLimitBytes);
            Assert.AreEqual(currentUsage, response.CurrentUsageBytes);
            Assert.AreEqual(storageLimit - currentUsage, response.AvailableSpaceBytes);
            Assert.AreEqual(25.0, response.UsagePercentage);
        }

        [TestMethod]
        public async Task GetTenantInfo_ForAdminTenant_ReturnsAdminStatus()
        {
            // Arrange
            string tenantId = "admin";
            long currentUsage = 1024 * 1024 * 5; // 5MB

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockStorageService.Setup(s => s.GetCurrentUsage(tenantId)).Returns(currentUsage);

            // Act
            IActionResult result = await _controller.GetTenantInfo(CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            OkObjectResult okResult = (OkObjectResult)result;
            Assert.IsNotNull(okResult.Value);

            TenantInfoResponse response = (TenantInfoResponse)okResult.Value;

            Assert.AreEqual(tenantId, response.TenantId);
            Assert.AreEqual("Admin User", response.DisplayName);
            Assert.IsTrue(response.IsAdmin);
            Assert.AreEqual(0, response.StorageLimitBytes); // Unlimited
            Assert.AreEqual(currentUsage, response.CurrentUsageBytes);
            Assert.AreEqual(0, response.AvailableSpaceBytes); // No limit, so 0 available
            Assert.AreEqual(0.0, response.UsagePercentage); // 0% usage for unlimited
        }

        [TestMethod]
        public async Task GetTenantInfo_WhenTenantNotFound_ReturnsNotFound()
        {
            // Arrange
            string tenantId = "nonexistent";
            _mockHttpContext.Object.Items["TenantId"] = tenantId;

            // Act
            IActionResult result = await _controller.GetTenantInfo(CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(NotFoundObjectResult));
            NotFoundObjectResult notFoundResult = (NotFoundObjectResult)result;
            Assert.AreEqual("Tenant not found", notFoundResult.Value);
        }

        [TestMethod]
        public async Task GetTenantInfo_WithTenantIdNotInContext_ThrowsInvalidOperationException()
        {
            // Arrange - No tenant ID in context

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                _controller.GetTenantInfo(CancellationToken.None));
        }

        [TestMethod]
        public async Task GetTenantInfo_DelegatesToServices()
        {
            // Arrange
            string tenantId = "tenant1";
            long currentUsage = 1024 * 1024 * 10; // 10MB

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockStorageService.Setup(s => s.GetCurrentUsage(tenantId)).Returns(currentUsage);

            // Act
            await _controller.GetTenantInfo(CancellationToken.None);

            // Assert
            _mockConfigService.Verify(c => c.GetConfiguration(), Times.Once);
            _mockStorageService.Verify(s => s.GetCurrentUsage(tenantId), Times.Once);
        }

        [TestMethod]
        public async Task GetStorageInfo_ReturnsStorageInformation()
        {
            // Arrange
            string tenantId = "tenant1";
            long currentUsage = 1024 * 1024 * 25; // 25MB
            long storageLimit = 1024 * 1024 * 100; // 100MB

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockStorageService.Setup(s => s.GetCurrentUsage(tenantId)).Returns(currentUsage);
            _mockStorageService.Setup(s => s.GetStorageLimit(tenantId)).Returns(storageLimit);

            // Act
            IActionResult result = await _controller.GetStorageInfo(CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            OkObjectResult okResult = (OkObjectResult)result;
            Assert.IsNotNull(okResult.Value);

            // Verify the response contains expected data
            TenantStorageInfo response = (TenantStorageInfo)okResult.Value;

            Assert.AreEqual(tenantId, response.TenantId);
            Assert.AreEqual(currentUsage, response.CurrentUsageBytes);
            Assert.AreEqual(storageLimit, response.StorageLimitBytes);
            Assert.AreEqual(storageLimit - currentUsage, response.AvailableSpaceBytes);
            Assert.AreEqual(25.0, response.UsagePercentage);
        }

        [TestMethod]
        public async Task GetStorageInfo_WithUnlimitedStorage_ReturnsCorrectValues()
        {
            // Arrange
            string tenantId = "admin";
            long currentUsage = 1024 * 1024 * 5; // 5MB
            long storageLimit = 0; // Unlimited

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockStorageService.Setup(s => s.GetCurrentUsage(tenantId)).Returns(currentUsage);
            _mockStorageService.Setup(s => s.GetStorageLimit(tenantId)).Returns(storageLimit);

            // Act
            IActionResult result = await _controller.GetStorageInfo(CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            OkObjectResult okResult = (OkObjectResult)result;
            Assert.IsNotNull(okResult.Value);

            TenantStorageInfo response = (TenantStorageInfo)okResult.Value;

            Assert.AreEqual(currentUsage, response.CurrentUsageBytes);
            Assert.AreEqual(storageLimit, response.StorageLimitBytes);
            Assert.AreEqual(0, response.AvailableSpaceBytes); // No limit, so 0 available
            Assert.AreEqual(0.0, response.UsagePercentage); // 0% usage for unlimited
        }

        [TestMethod]
        public async Task CanStoreFile_WhenCanStore_ReturnsTrue()
        {
            // Arrange
            string tenantId = "tenant1";
            long fileSize = 1024 * 1024 * 10; // 10MB
            long currentUsage = 1024 * 1024 * 20; // 20MB
            long storageLimit = 1024 * 1024 * 100; // 100MB

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockStorageService.Setup(s => s.CanStoreData(tenantId, fileSize)).Returns(true);
            _mockStorageService.Setup(s => s.GetCurrentUsage(tenantId)).Returns(currentUsage);
            _mockStorageService.Setup(s => s.GetStorageLimit(tenantId)).Returns(storageLimit);

            // Act
            IActionResult result = await _controller.CanStoreFile(fileSize, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            OkObjectResult okResult = (OkObjectResult)result;
            Assert.IsNotNull(okResult.Value);

            QuotaCheckResult response = (QuotaCheckResult)okResult.Value;

            Assert.IsTrue(response.CanStore);
            Assert.AreEqual(fileSize, response.FileSizeBytes);
            Assert.IsFalse(response.WouldExceedQuota);
        }

        [TestMethod]
        public async Task CanStoreFile_WhenCannotStore_ReturnsFalse()
        {
            // Arrange
            string tenantId = "tenant1";
            long fileSize = 1024 * 1024 * 90; // 90MB
            long currentUsage = 1024 * 1024 * 20; // 20MB
            long storageLimit = 1024 * 1024 * 100; // 100MB

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockStorageService.Setup(s => s.CanStoreData(tenantId, fileSize)).Returns(false);
            _mockStorageService.Setup(s => s.GetCurrentUsage(tenantId)).Returns(currentUsage);
            _mockStorageService.Setup(s => s.GetStorageLimit(tenantId)).Returns(storageLimit);

            // Act
            IActionResult result = await _controller.CanStoreFile(fileSize, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            OkObjectResult okResult = (OkObjectResult)result;
            Assert.IsNotNull(okResult.Value);

            QuotaCheckResult response = (QuotaCheckResult)okResult.Value;

            Assert.IsFalse(response.CanStore);
            Assert.AreEqual(fileSize, response.FileSizeBytes);
            Assert.IsTrue(response.WouldExceedQuota);
        }

        [TestMethod]
        public async Task CanStoreFile_WithZeroFileSize_ReturnsTrue()
        {
            // Arrange
            string tenantId = "tenant1";
            long fileSize = 0;
            long currentUsage = 1024 * 1024 * 20; // 20MB
            long storageLimit = 1024 * 1024 * 100; // 100MB

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockStorageService.Setup(s => s.CanStoreData(tenantId, fileSize)).Returns(true);
            _mockStorageService.Setup(s => s.GetCurrentUsage(tenantId)).Returns(currentUsage);
            _mockStorageService.Setup(s => s.GetStorageLimit(tenantId)).Returns(storageLimit);

            // Act
            IActionResult result = await _controller.CanStoreFile(fileSize, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            OkObjectResult okResult = (OkObjectResult)result;
            Assert.IsNotNull(okResult.Value);

            QuotaCheckResult response = (QuotaCheckResult)okResult.Value;

            Assert.IsTrue(response.CanStore);
            Assert.AreEqual(fileSize, response.FileSizeBytes);
        }

        [TestMethod]
        public async Task CanStoreFile_WithNegativeFileSize_HandlesGracefully()
        {
            // Arrange
            string tenantId = "tenant1";
            long fileSize = -1024;
            long currentUsage = 1024 * 1024 * 20; // 20MB
            long storageLimit = 1024 * 1024 * 100; // 100MB

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockStorageService.Setup(s => s.CanStoreData(tenantId, fileSize)).Returns(true);
            _mockStorageService.Setup(s => s.GetCurrentUsage(tenantId)).Returns(currentUsage);
            _mockStorageService.Setup(s => s.GetStorageLimit(tenantId)).Returns(storageLimit);

            // Act
            IActionResult result = await _controller.CanStoreFile(fileSize, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            OkObjectResult okResult = (OkObjectResult)result;
            Assert.IsNotNull(okResult.Value);

            QuotaCheckResult response = (QuotaCheckResult)okResult.Value;
            Assert.AreEqual(fileSize, response.FileSizeBytes);
        }

        [TestMethod]
        public async Task GetStorageInfo_WithTenantIdNotInContext_ThrowsInvalidOperationException()
        {
            // Arrange
            // Don't add TenantId to Items dictionary to simulate missing tenant ID

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _controller.GetStorageInfo(CancellationToken.None));
        }

        [TestMethod]
        public async Task CanStoreFile_WithTenantIdNotInContext_ThrowsInvalidOperationException()
        {
            // Arrange
            // Don't add TenantId to Items dictionary to simulate missing tenant ID

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _controller.CanStoreFile(1024, CancellationToken.None));
        }

        [TestMethod]
        public async Task GetStorageInfo_DelegatesToStorageService()
        {
            // Arrange
            string tenantId = "tenant1";
            long currentUsage = 1024 * 1024 * 25;
            long storageLimit = 1024 * 1024 * 100;

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockStorageService.Setup(s => s.GetCurrentUsage(tenantId)).Returns(currentUsage);
            _mockStorageService.Setup(s => s.GetStorageLimit(tenantId)).Returns(storageLimit);

            // Act
            await _controller.GetStorageInfo(CancellationToken.None);

            // Assert
            _mockStorageService.Verify(s => s.GetCurrentUsage(tenantId), Times.Once);
            _mockStorageService.Verify(s => s.GetStorageLimit(tenantId), Times.Once);
        }

        [TestMethod]
        public async Task CanStoreFile_DelegatesToStorageService()
        {
            // Arrange
            string tenantId = "tenant1";
            long fileSize = 1024 * 1024 * 10;
            long currentUsage = 1024 * 1024 * 20;
            long storageLimit = 1024 * 1024 * 100;

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockStorageService.Setup(s => s.CanStoreData(tenantId, fileSize)).Returns(true);
            _mockStorageService.Setup(s => s.GetCurrentUsage(tenantId)).Returns(currentUsage);
            _mockStorageService.Setup(s => s.GetStorageLimit(tenantId)).Returns(storageLimit);

            // Act
            await _controller.CanStoreFile(fileSize, CancellationToken.None);

            // Assert
            _mockStorageService.Verify(s => s.CanStoreData(tenantId, fileSize), Times.Once);
            _mockStorageService.Verify(s => s.GetCurrentUsage(tenantId), Times.Once);
            _mockStorageService.Verify(s => s.GetStorageLimit(tenantId), Times.Once);
        }
    }
}