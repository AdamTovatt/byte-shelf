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
        private Mock<IStorageService> _mockStorageService = null!;
        private Mock<ITenantConfigurationService> _mockConfigService = null!;
        private Mock<HttpContext> _mockHttpContext = null!;
        private TenantConfiguration _tenantConfig = null!;

        [TestInitialize]
        public void Setup()
        {
            _mockStorageService = new Mock<IStorageService>();
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
            _mockConfigService.Setup(c => c.GetTenant(tenantId)).Returns(_tenantConfig.Tenants[tenantId]);

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
            _mockConfigService.Setup(c => c.GetTenant(tenantId)).Returns(_tenantConfig.Tenants[tenantId]);

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
            _mockConfigService.Setup(c => c.GetTenant(tenantId)).Returns(_tenantConfig.Tenants[tenantId]);

            // Act
            await _controller.GetTenantInfo(CancellationToken.None);

            // Assert
            _mockConfigService.Verify(c => c.GetTenant(tenantId), Times.Once);
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

        [TestMethod]
        public async Task GetSubTenants_ReturnsSubTenants_WhenTenantHasSubTenants()
        {
            // Arrange
            string tenantId = "tenant1";
            Dictionary<string, TenantInfo> subTenants = new Dictionary<string, TenantInfo>
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

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockConfigService.Setup(c => c.GetSubTenants(tenantId)).Returns(subTenants);

            // Act
            IActionResult result = await _controller.GetSubTenants(CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            OkObjectResult okResult = (OkObjectResult)result;
            Assert.IsNotNull(okResult.Value);

            Dictionary<string, TenantInfo> response = (Dictionary<string, TenantInfo>)okResult.Value;
            Assert.AreEqual(2, response.Count);
            Assert.IsTrue(response.ContainsKey("subtenant1"));
            Assert.IsTrue(response.ContainsKey("subtenant2"));
            Assert.AreEqual("Sub Tenant 1", response["subtenant1"].DisplayName);
            Assert.AreEqual("Sub Tenant 2", response["subtenant2"].DisplayName);
        }

        [TestMethod]
        public async Task GetSubTenants_ReturnsEmptyDictionary_WhenTenantHasNoSubTenants()
        {
            // Arrange
            string tenantId = "tenant1";
            Dictionary<string, TenantInfo> emptySubTenants = new Dictionary<string, TenantInfo>();

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockConfigService.Setup(c => c.GetSubTenants(tenantId)).Returns(emptySubTenants);

            // Act
            IActionResult result = await _controller.GetSubTenants(CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            OkObjectResult okResult = (OkObjectResult)result;
            Assert.IsNotNull(okResult.Value);

            Dictionary<string, TenantInfo> response = (Dictionary<string, TenantInfo>)okResult.Value;
            Assert.AreEqual(0, response.Count);
        }

        [TestMethod]
        public async Task GetSubTenants_WithTenantIdNotInContext_ThrowsInvalidOperationException()
        {
            // Arrange - No tenant ID in context

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                _controller.GetSubTenants(CancellationToken.None));
        }

        [TestMethod]
        public async Task GetSubTenant_ReturnsSubTenant_WhenSubTenantExists()
        {
            // Arrange
            string tenantId = "tenant1";
            string subTenantId = "subtenant1";
            TenantInfo subTenant = new TenantInfo
            {
                ApiKey = "sub-key",
                DisplayName = "Sub Tenant",
                StorageLimitBytes = 1024 * 1024 * 50,
                IsAdmin = false
            };

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockConfigService.Setup(c => c.GetSubTenant(tenantId, subTenantId)).Returns(subTenant!);

            // Act
            IActionResult result = await _controller.GetSubTenant(subTenantId, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            OkObjectResult okResult = (OkObjectResult)result;
            Assert.IsNotNull(okResult.Value);

            TenantInfo response = (TenantInfo)okResult.Value;
            Assert.AreEqual(subTenant.DisplayName, response.DisplayName);
            Assert.AreEqual(subTenant.ApiKey, response.ApiKey);
            Assert.AreEqual(subTenant.StorageLimitBytes, response.StorageLimitBytes);
            Assert.AreEqual(subTenant.IsAdmin, response.IsAdmin);
            Assert.AreEqual(subTenant.Parent, response.Parent);
        }

        [TestMethod]
        public async Task GetSubTenant_ReturnsNotFound_WhenSubTenantDoesNotExist()
        {
            // Arrange
            string tenantId = "tenant1";
            string subTenantId = "nonexistent";

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockConfigService.Setup(c => c.GetSubTenant(tenantId, subTenantId)).Returns((TenantInfo?)null);

            // Act
            IActionResult result = await _controller.GetSubTenant(subTenantId, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(NotFoundObjectResult));
            NotFoundObjectResult notFoundResult = (NotFoundObjectResult)result;
            Assert.AreEqual("Subtenant not found", notFoundResult.Value);
        }

        [TestMethod]
        public async Task GetSubTenant_WithTenantIdNotInContext_ThrowsInvalidOperationException()
        {
            // Arrange - No tenant ID in context

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                _controller.GetSubTenant("subtenant1", CancellationToken.None));
        }

        [TestMethod]
        public async Task CreateSubTenant_CreatesSubTenant_WhenValidRequest()
        {
            // Arrange
            string tenantId = "tenant1";
            string displayName = "New Subtenant";
            string newSubTenantId = "new-subtenant-id";

            CreateSubTenantRequest request = new CreateSubTenantRequest
            {
                DisplayName = displayName
            };

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockConfigService.Setup(c => c.CreateSubTenantAsync(tenantId, displayName))
                .ReturnsAsync(newSubTenantId);

            // Act
            IActionResult result = await _controller.CreateSubTenant(request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(CreatedAtActionResult));
            CreatedAtActionResult createdResult = (CreatedAtActionResult)result;
            Assert.AreEqual("GetSubTenant", createdResult.ActionName);
            Assert.AreEqual(newSubTenantId, createdResult.RouteValues!["subTenantId"]);

            CreateSubTenantResponse response = (CreateSubTenantResponse)createdResult.Value!;
            Assert.AreEqual(newSubTenantId, response.TenantId);
            Assert.AreEqual(displayName, response.DisplayName);
        }

        [TestMethod]
        public async Task CreateSubTenant_ReturnsBadRequest_WhenRequestIsNull()
        {
            // Arrange
            string tenantId = "tenant1";
            _mockHttpContext.Object.Items["TenantId"] = tenantId;

            // Act
            IActionResult result = await _controller.CreateSubTenant(null!, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            BadRequestObjectResult badRequestResult = (BadRequestObjectResult)result;
            Assert.AreEqual("Request cannot be null", badRequestResult.Value);
        }

        [TestMethod]
        public async Task CreateSubTenant_ReturnsBadRequest_WhenDisplayNameIsNull()
        {
            // Arrange
            string tenantId = "tenant1";
            CreateSubTenantRequest request = new CreateSubTenantRequest
            {
                DisplayName = null!
            };

            _mockHttpContext.Object.Items["TenantId"] = tenantId;

            // Act
            IActionResult result = await _controller.CreateSubTenant(request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            BadRequestObjectResult badRequestResult = (BadRequestObjectResult)result;
            Assert.AreEqual("Display name is required", badRequestResult.Value);
        }

        [TestMethod]
        public async Task CreateSubTenant_ReturnsBadRequest_WhenDisplayNameIsEmpty()
        {
            // Arrange
            string tenantId = "tenant1";
            CreateSubTenantRequest request = new CreateSubTenantRequest
            {
                DisplayName = ""
            };

            _mockHttpContext.Object.Items["TenantId"] = tenantId;

            // Act
            IActionResult result = await _controller.CreateSubTenant(request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            BadRequestObjectResult badRequestResult = (BadRequestObjectResult)result;
            Assert.AreEqual("Display name is required", badRequestResult.Value);
        }

        [TestMethod]
        public async Task CreateSubTenant_ReturnsBadRequest_WhenDisplayNameIsWhitespace()
        {
            // Arrange
            string tenantId = "tenant1";
            CreateSubTenantRequest request = new CreateSubTenantRequest
            {
                DisplayName = "   "
            };

            _mockHttpContext.Object.Items["TenantId"] = tenantId;

            // Act
            IActionResult result = await _controller.CreateSubTenant(request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            BadRequestObjectResult badRequestResult = (BadRequestObjectResult)result;
            Assert.AreEqual("Display name is required", badRequestResult.Value);
        }

        [TestMethod]
        public async Task CreateSubTenant_ReturnsBadRequest_WhenServiceThrowsInvalidOperationException()
        {
            // Arrange
            string tenantId = "tenant1";
            string displayName = "New Subtenant";
            string errorMessage = "Cannot create subtenant: maximum depth reached";

            CreateSubTenantRequest request = new CreateSubTenantRequest
            {
                DisplayName = displayName
            };

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockConfigService.Setup(c => c.CreateSubTenantAsync(tenantId, displayName))
                .ThrowsAsync(new InvalidOperationException(errorMessage));

            // Act
            IActionResult result = await _controller.CreateSubTenant(request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            BadRequestObjectResult badRequestResult = (BadRequestObjectResult)result;
            Assert.AreEqual(errorMessage, badRequestResult.Value);
        }

        [TestMethod]
        public async Task CreateSubTenant_WithTenantIdNotInContext_ThrowsInvalidOperationException()
        {
            // Arrange - No tenant ID in context
            CreateSubTenantRequest request = new CreateSubTenantRequest
            {
                DisplayName = "New Subtenant"
            };

            // Act & Assert
            IActionResult response = await _controller.CreateSubTenant(request, CancellationToken.None);
            Assert.IsTrue(response.GetType() == typeof(BadRequestObjectResult), "Should create a bad request response without tenant id in context");
        }

        [TestMethod]
        public async Task UpdateSubTenantStorageLimit_UpdatesStorageLimit_WhenValidRequest()
        {
            // Arrange
            string tenantId = "tenant1";
            string subTenantId = "subtenant1";
            long newStorageLimit = 1024 * 1024 * 200; // 200MB

            UpdateStorageLimitRequest request = new UpdateStorageLimitRequest
            {
                StorageLimitBytes = newStorageLimit
            };

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockConfigService.Setup(c => c.UpdateSubTenantStorageLimitAsync(tenantId, subTenantId, newStorageLimit))
                .ReturnsAsync(true);

            // Act
            IActionResult result = await _controller.UpdateSubTenantStorageLimit(subTenantId, request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkResult));
        }

        [TestMethod]
        public async Task UpdateSubTenantStorageLimit_ReturnsBadRequest_WhenRequestIsNull()
        {
            // Arrange
            string tenantId = "tenant1";
            string subTenantId = "subtenant1";

            _mockHttpContext.Object.Items["TenantId"] = tenantId;

            // Act
            IActionResult result = await _controller.UpdateSubTenantStorageLimit(subTenantId, null!, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            BadRequestObjectResult badRequestResult = (BadRequestObjectResult)result;
            Assert.AreEqual("Request cannot be null", badRequestResult.Value);
        }

        [TestMethod]
        public async Task UpdateSubTenantStorageLimit_ReturnsBadRequest_WhenStorageLimitIsNegative()
        {
            // Arrange
            string tenantId = "tenant1";
            string subTenantId = "subtenant1";
            long negativeStorageLimit = -1024;

            UpdateStorageLimitRequest request = new UpdateStorageLimitRequest
            {
                StorageLimitBytes = negativeStorageLimit
            };

            _mockHttpContext.Object.Items["TenantId"] = tenantId;

            // Act
            IActionResult result = await _controller.UpdateSubTenantStorageLimit(subTenantId, request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            BadRequestObjectResult badRequestResult = (BadRequestObjectResult)result;
            Assert.AreEqual("Storage limit must be non-negative", badRequestResult.Value);
        }

        [TestMethod]
        public async Task UpdateSubTenantStorageLimit_ReturnsNotFound_WhenSubTenantDoesNotExist()
        {
            // Arrange
            string tenantId = "tenant1";
            string subTenantId = "nonexistent";
            long newStorageLimit = 1024 * 1024 * 200;

            UpdateStorageLimitRequest request = new UpdateStorageLimitRequest
            {
                StorageLimitBytes = newStorageLimit
            };

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockConfigService.Setup(c => c.UpdateSubTenantStorageLimitAsync(tenantId, subTenantId, newStorageLimit))
                .ReturnsAsync(false);

            // Act
            IActionResult result = await _controller.UpdateSubTenantStorageLimit(subTenantId, request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(NotFoundObjectResult));
            NotFoundObjectResult notFoundResult = (NotFoundObjectResult)result;
            Assert.AreEqual("Subtenant not found", notFoundResult.Value);
        }

        [TestMethod]
        public async Task UpdateSubTenantStorageLimit_WithTenantIdNotInContext_ThrowsInvalidOperationException()
        {
            // Arrange - No tenant ID in context
            string subTenantId = "subtenant1";
            UpdateStorageLimitRequest request = new UpdateStorageLimitRequest
            {
                StorageLimitBytes = 1024 * 1024 * 200
            };

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                _controller.UpdateSubTenantStorageLimit(subTenantId, request, CancellationToken.None));
        }

        [TestMethod]
        public async Task DeleteSubTenant_DeletesSubTenant_WhenSubTenantExists()
        {
            // Arrange
            string tenantId = "tenant1";
            string subTenantId = "subtenant1";

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockConfigService.Setup(c => c.DeleteSubTenantAsync(tenantId, subTenantId))
                .ReturnsAsync(true);

            // Act
            IActionResult result = await _controller.DeleteSubTenant(subTenantId, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkResult));
        }

        [TestMethod]
        public async Task DeleteSubTenant_ReturnsNotFound_WhenSubTenantDoesNotExist()
        {
            // Arrange
            string tenantId = "tenant1";
            string subTenantId = "nonexistent";

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockConfigService.Setup(c => c.DeleteSubTenantAsync(tenantId, subTenantId))
                .ReturnsAsync(false);

            // Act
            IActionResult result = await _controller.DeleteSubTenant(subTenantId, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(NotFoundObjectResult));
            NotFoundObjectResult notFoundResult = (NotFoundObjectResult)result;
            Assert.AreEqual("Subtenant not found", notFoundResult.Value);
        }

        [TestMethod]
        public async Task DeleteSubTenant_WithTenantIdNotInContext_ThrowsInvalidOperationException()
        {
            // Arrange - No tenant ID in context
            string subTenantId = "subtenant1";

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                _controller.DeleteSubTenant(subTenantId, CancellationToken.None));
        }

        [TestMethod]
        public async Task GetSubTenants_DelegatesToConfigService()
        {
            // Arrange
            string tenantId = "tenant1";
            Dictionary<string, TenantInfo> subTenants = new Dictionary<string, TenantInfo>();

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockConfigService.Setup(c => c.GetSubTenants(tenantId)).Returns(subTenants);

            // Act
            await _controller.GetSubTenants(CancellationToken.None);

            // Assert
            _mockConfigService.Verify(c => c.GetSubTenants(tenantId), Times.Once);
        }

        [TestMethod]
        public async Task GetSubTenant_DelegatesToConfigService()
        {
            // Arrange
            string tenantId = "tenant1";
            string subTenantId = "subtenant1";
            TenantInfo subTenant = new TenantInfo();

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockConfigService.Setup(c => c.GetSubTenant(tenantId, subTenantId)).Returns(subTenant);

            // Act
            await _controller.GetSubTenant(subTenantId, CancellationToken.None);

            // Assert
            _mockConfigService.Verify(c => c.GetSubTenant(tenantId, subTenantId), Times.Once);
        }

        [TestMethod]
        public async Task CreateSubTenant_DelegatesToConfigService()
        {
            // Arrange
            string tenantId = "tenant1";
            string displayName = "New Subtenant";
            string newSubTenantId = "new-subtenant-id";

            CreateSubTenantRequest request = new CreateSubTenantRequest
            {
                DisplayName = displayName
            };

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockConfigService.Setup(c => c.CreateSubTenantAsync(tenantId, displayName))
                .ReturnsAsync(newSubTenantId);

            // Act
            await _controller.CreateSubTenant(request, CancellationToken.None);

            // Assert
            _mockConfigService.Verify(c => c.CreateSubTenantAsync(tenantId, displayName), Times.Once);
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_CreatesSubTenant_WhenValidRequest()
        {
            // Arrange
            string tenantId = "tenant1";
            string parentSubtenantId = "parent-subtenant";
            string displayName = "New Subtenant";
            string newSubTenantId = "new-subtenant-id";

            CreateSubTenantRequest request = new CreateSubTenantRequest
            {
                DisplayName = displayName
            };

            TenantInfo parentSubtenant = new TenantInfo
            {
                ApiKey = "parent-key",
                DisplayName = "Parent Subtenant",
                StorageLimitBytes = 1024 * 1024 * 100,
                IsAdmin = false
            };

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockConfigService.Setup(c => c.GetSubTenant(tenantId, parentSubtenantId)).Returns(parentSubtenant);
            _mockConfigService.Setup(c => c.CreateSubTenantAsync(parentSubtenantId, displayName))
                .ReturnsAsync(newSubTenantId);

            // Act
            IActionResult result = await _controller.CreateSubTenantUnderSubTenant(parentSubtenantId, request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(CreatedAtActionResult));
            CreatedAtActionResult createdResult = (CreatedAtActionResult)result;
            Assert.AreEqual("GetSubTenant", createdResult.ActionName);
            Assert.AreEqual(newSubTenantId, createdResult.RouteValues!["subTenantId"]);

            CreateSubTenantResponse response = (CreateSubTenantResponse)createdResult.Value!;
            Assert.AreEqual(newSubTenantId, response.TenantId);
            Assert.AreEqual(displayName, response.DisplayName);
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_ReturnsBadRequest_WhenRequestIsNull()
        {
            // Arrange
            string tenantId = "tenant1";
            string parentSubtenantId = "parent-subtenant";
            _mockHttpContext.Object.Items["TenantId"] = tenantId;

            // Act
            IActionResult result = await _controller.CreateSubTenantUnderSubTenant(parentSubtenantId, null!, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            BadRequestObjectResult badRequestResult = (BadRequestObjectResult)result;
            Assert.AreEqual("Request cannot be null", badRequestResult.Value);
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_ReturnsBadRequest_WhenDisplayNameIsNull()
        {
            // Arrange
            string tenantId = "tenant1";
            string parentSubtenantId = "parent-subtenant";
            CreateSubTenantRequest request = new CreateSubTenantRequest
            {
                DisplayName = null!
            };

            _mockHttpContext.Object.Items["TenantId"] = tenantId;

            // Act
            IActionResult result = await _controller.CreateSubTenantUnderSubTenant(parentSubtenantId, request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            BadRequestObjectResult badRequestResult = (BadRequestObjectResult)result;
            Assert.AreEqual("Display name is required", badRequestResult.Value);
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_ReturnsBadRequest_WhenDisplayNameIsEmpty()
        {
            // Arrange
            string tenantId = "tenant1";
            string parentSubtenantId = "parent-subtenant";
            CreateSubTenantRequest request = new CreateSubTenantRequest
            {
                DisplayName = ""
            };

            _mockHttpContext.Object.Items["TenantId"] = tenantId;

            // Act
            IActionResult result = await _controller.CreateSubTenantUnderSubTenant(parentSubtenantId, request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            BadRequestObjectResult badRequestResult = (BadRequestObjectResult)result;
            Assert.AreEqual("Display name is required", badRequestResult.Value);
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_ReturnsBadRequest_WhenDisplayNameIsWhitespace()
        {
            // Arrange
            string tenantId = "tenant1";
            string parentSubtenantId = "parent-subtenant";
            CreateSubTenantRequest request = new CreateSubTenantRequest
            {
                DisplayName = "   "
            };

            _mockHttpContext.Object.Items["TenantId"] = tenantId;

            // Act
            IActionResult result = await _controller.CreateSubTenantUnderSubTenant(parentSubtenantId, request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            BadRequestObjectResult badRequestResult = (BadRequestObjectResult)result;
            Assert.AreEqual("Display name is required", badRequestResult.Value);
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_ReturnsBadRequest_WhenParentSubtenantIdIsNull()
        {
            // Arrange
            string tenantId = "tenant1";
            CreateSubTenantRequest request = new CreateSubTenantRequest
            {
                DisplayName = "New Subtenant"
            };

            _mockHttpContext.Object.Items["TenantId"] = tenantId;

            // Act
            IActionResult result = await _controller.CreateSubTenantUnderSubTenant(null!, request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            BadRequestObjectResult badRequestResult = (BadRequestObjectResult)result;
            Assert.AreEqual("Parent subtenant ID is required", badRequestResult.Value);
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_ReturnsBadRequest_WhenParentSubtenantIdIsEmpty()
        {
            // Arrange
            string tenantId = "tenant1";
            string parentSubtenantId = "";
            CreateSubTenantRequest request = new CreateSubTenantRequest
            {
                DisplayName = "New Subtenant"
            };

            _mockHttpContext.Object.Items["TenantId"] = tenantId;

            // Act
            IActionResult result = await _controller.CreateSubTenantUnderSubTenant(parentSubtenantId, request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            BadRequestObjectResult badRequestResult = (BadRequestObjectResult)result;
            Assert.AreEqual("Parent subtenant ID is required", badRequestResult.Value);
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_ReturnsNotFound_WhenParentSubtenantDoesNotExist()
        {
            // Arrange
            string tenantId = "tenant1";
            string parentSubtenantId = "nonexistent";
            CreateSubTenantRequest request = new CreateSubTenantRequest
            {
                DisplayName = "New Subtenant"
            };

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockConfigService.Setup(c => c.GetSubTenant(tenantId, parentSubtenantId)).Returns((TenantInfo?)null);

            // Act
            IActionResult result = await _controller.CreateSubTenantUnderSubTenant(parentSubtenantId, request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(NotFoundObjectResult));
            NotFoundObjectResult notFoundResult = (NotFoundObjectResult)result;
            Assert.AreEqual("Parent subtenant not found", notFoundResult.Value);
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_ReturnsBadRequest_WhenServiceThrowsInvalidOperationException()
        {
            // Arrange
            string tenantId = "tenant1";
            string parentSubtenantId = "parent-subtenant";
            string displayName = "New Subtenant";
            string errorMessage = "Cannot create subtenant: maximum depth reached";

            CreateSubTenantRequest request = new CreateSubTenantRequest
            {
                DisplayName = displayName
            };

            TenantInfo parentSubtenant = new TenantInfo
            {
                ApiKey = "parent-key",
                DisplayName = "Parent Subtenant",
                StorageLimitBytes = 1024 * 1024 * 100,
                IsAdmin = false
            };

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockConfigService.Setup(c => c.GetSubTenant(tenantId, parentSubtenantId)).Returns(parentSubtenant);
            _mockConfigService.Setup(c => c.CreateSubTenantAsync(parentSubtenantId, displayName))
                .ThrowsAsync(new InvalidOperationException(errorMessage));

            // Act
            IActionResult result = await _controller.CreateSubTenantUnderSubTenant(parentSubtenantId, request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            BadRequestObjectResult badRequestResult = (BadRequestObjectResult)result;
            Assert.AreEqual(errorMessage, badRequestResult.Value);
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_WithTenantIdNotInContext_ThrowsInvalidOperationException()
        {
            // Arrange - No tenant ID in context
            string parentSubtenantId = "parent-subtenant";
            CreateSubTenantRequest request = new CreateSubTenantRequest
            {
                DisplayName = "New Subtenant"
            };

            // Act & Assert
            IActionResult response = await _controller.CreateSubTenantUnderSubTenant(parentSubtenantId, request, CancellationToken.None);
            Assert.IsTrue(response.GetType() == typeof(BadRequestObjectResult), "Should create a bad request response without tenant id in context");
        }

        [TestMethod]
        public async Task CreateSubTenantUnderSubTenant_DelegatesToConfigService()
        {
            // Arrange
            string tenantId = "tenant1";
            string parentSubtenantId = "parent-subtenant";
            string displayName = "New Subtenant";
            string newSubTenantId = "new-subtenant-id";

            CreateSubTenantRequest request = new CreateSubTenantRequest
            {
                DisplayName = displayName
            };

            TenantInfo parentSubtenant = new TenantInfo
            {
                ApiKey = "parent-key",
                DisplayName = "Parent Subtenant",
                StorageLimitBytes = 1024 * 1024 * 100,
                IsAdmin = false
            };

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockConfigService.Setup(c => c.GetSubTenant(tenantId, parentSubtenantId)).Returns(parentSubtenant);
            _mockConfigService.Setup(c => c.CreateSubTenantAsync(parentSubtenantId, displayName))
                .ReturnsAsync(newSubTenantId);

            // Act
            await _controller.CreateSubTenantUnderSubTenant(parentSubtenantId, request, CancellationToken.None);

            // Assert
            _mockConfigService.Verify(c => c.GetSubTenant(tenantId, parentSubtenantId), Times.Once);
            _mockConfigService.Verify(c => c.CreateSubTenantAsync(parentSubtenantId, displayName), Times.Once);
        }

        [TestMethod]
        public async Task UpdateSubTenantStorageLimit_DelegatesToConfigService()
        {
            // Arrange
            string tenantId = "tenant1";
            string subTenantId = "subtenant1";
            long newStorageLimit = 1024 * 1024 * 200;

            UpdateStorageLimitRequest request = new UpdateStorageLimitRequest
            {
                StorageLimitBytes = newStorageLimit
            };

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockConfigService.Setup(c => c.UpdateSubTenantStorageLimitAsync(tenantId, subTenantId, newStorageLimit))
                .ReturnsAsync(true);

            // Act
            await _controller.UpdateSubTenantStorageLimit(subTenantId, request, CancellationToken.None);

            // Assert
            _mockConfigService.Verify(c => c.UpdateSubTenantStorageLimitAsync(tenantId, subTenantId, newStorageLimit), Times.Once);
        }

        [TestMethod]
        public async Task DeleteSubTenant_DelegatesToConfigService()
        {
            // Arrange
            string tenantId = "tenant1";
            string subTenantId = "subtenant1";

            _mockHttpContext.Object.Items["TenantId"] = tenantId;
            _mockConfigService.Setup(c => c.DeleteSubTenantAsync(tenantId, subTenantId))
                .ReturnsAsync(true);

            // Act
            await _controller.DeleteSubTenant(subTenantId, CancellationToken.None);

            // Assert
            _mockConfigService.Verify(c => c.DeleteSubTenantAsync(tenantId, subTenantId), Times.Once);
        }
    }
}