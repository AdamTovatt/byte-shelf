using ByteShelf.Configuration;
using ByteShelf.Controllers;
using ByteShelf.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Text.Json;

namespace ByteShelf.Tests
{
    [TestClass]
    public class AdminControllerTests
    {
        private AdminController _controller = null!;
        private Mock<ITenantConfigurationService> _mockConfigService = null!;
        private Mock<IStorageService> _mockStorageService = null!;
        private Mock<IFileStorageService> _mockFileStorageService = null!;
        private Mock<HttpContext> _mockHttpContext = null!;
        private TenantConfiguration _tenantConfig = null!;

        [TestInitialize]
        public void Setup()
        {
            _mockConfigService = new Mock<ITenantConfigurationService>();
            _mockStorageService = new Mock<IStorageService>();
            _mockFileStorageService = new Mock<IFileStorageService>();
            _mockHttpContext = new Mock<HttpContext>();

            // Setup the Items dictionary properly
            Dictionary<object, object?> items = new Dictionary<object, object?>();
            _mockHttpContext.Setup(c => c.Items).Returns(items);

            // Setup default tenant configuration
            _tenantConfig = new TenantConfiguration
            {
                RequireAuthentication = true,
                Tenants = new Dictionary<string, TenantInfo>
                {
                    ["admin"] = new TenantInfo
                    {
                        ApiKey = "admin-key",
                        DisplayName = "Admin Tenant",
                        StorageLimitBytes = 0, // Unlimited
                        IsAdmin = true,
                    },
                    ["tenant1"] = new TenantInfo
                    {
                        ApiKey = "tenant1-key",
                        DisplayName = "Tenant 1",
                        StorageLimitBytes = 1024 * 1024 * 100, // 100MB
                        IsAdmin = false,
                    },
                    ["tenant2"] = new TenantInfo
                    {
                        ApiKey = "tenant2-key",
                        DisplayName = "Tenant 2",
                        StorageLimitBytes = 1024 * 1024 * 50, // 50MB
                        IsAdmin = false,
                    }
                }
            };

            _mockConfigService.Setup(c => c.GetConfiguration()).Returns(_tenantConfig);
            _mockStorageService.Setup(s => s.GetTotalUsageIncludingSubTenants("tenant1")).Returns(1024 * 1024 * 25); // 25MB used
            _mockStorageService.Setup(s => s.GetTotalUsageIncludingSubTenants("tenant2")).Returns(1024 * 1024 * 10); // 10MB used
            _mockStorageService.Setup(s => s.GetTotalUsageIncludingSubTenants("admin")).Returns(1024 * 1024 * 5); // 5MB used

            _controller = new AdminController(_mockConfigService.Object, _mockStorageService.Object, _mockFileStorageService.Object);
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = _mockHttpContext.Object
            };
        }

        [TestMethod]
        public void Constructor_WithNullConfigService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new AdminController(null!, _mockStorageService.Object, _mockFileStorageService.Object));
        }

        [TestMethod]
        public void Constructor_WithNullStorageService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new AdminController(_mockConfigService.Object, null!, _mockFileStorageService.Object));
        }

        [TestMethod]
        public void Constructor_WithNullFileStorageService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new AdminController(_mockConfigService.Object, _mockStorageService.Object, null!));
        }

        [TestMethod]
        public async Task GetTenants_WhenUserIsAdmin_ReturnsAllTenants()
        {
            // Arrange
            SetupAdminUser();

            // Act
            IActionResult result = await _controller.GetTenants(CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            OkObjectResult okResult = (OkObjectResult)result;
            Assert.IsNotNull(okResult.Value);

            // Verify the response contains all tenants
            string response = JsonSerializer.Serialize(okResult.Value);
            Assert.IsTrue(response.Contains("admin"));
            Assert.IsTrue(response.Contains("tenant1"));
            Assert.IsTrue(response.Contains("tenant2"));
        }

        [TestMethod]
        public async Task GetTenants_WhenUserIsNotAdmin_ReturnsForbid()
        {
            // Arrange
            SetupNonAdminUser();

            // Act
            IActionResult result = await _controller.GetTenants(CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ForbidResult));
        }

        [TestMethod]
        public async Task GetTenant_WhenUserIsAdminAndTenantExists_ReturnsTenantInfo()
        {
            // Arrange
            SetupAdminUser();

            // Act
            IActionResult result = await _controller.GetTenant("tenant1", CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            OkObjectResult okResult = (OkObjectResult)result;
            Assert.IsNotNull(okResult.Value);

            string response = JsonSerializer.Serialize(okResult.Value);
            Assert.IsTrue(response.Contains("tenant1"));
            Assert.IsTrue(response.Contains("Tenant 1"));
        }

        [TestMethod]
        public async Task GetTenant_WhenUserIsAdminAndTenantDoesNotExist_ReturnsNotFound()
        {
            // Arrange
            SetupAdminUser();

            // Act
            IActionResult result = await _controller.GetTenant("nonexistent", CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(NotFoundObjectResult));
            NotFoundObjectResult notFoundResult = (NotFoundObjectResult)result;
            Assert.AreEqual("Tenant not found", notFoundResult.Value);
        }

        [TestMethod]
        public async Task GetTenant_WhenUserIsNotAdmin_ReturnsForbid()
        {
            // Arrange
            SetupNonAdminUser();

            // Act
            IActionResult result = await _controller.GetTenant("tenant1", CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ForbidResult));
        }

        [TestMethod]
        public async Task CreateTenant_WhenUserIsAdminAndValidRequest_CreatesTenant()
        {
            // Arrange
            SetupAdminUser();
            CreateTenantRequest request = new CreateTenantRequest
            {
                TenantId = "newtenant",
                ApiKey = "new-key",
                DisplayName = "New Tenant",
                StorageLimitBytes = 1024 * 1024 * 200, // 200MB
                IsAdmin = false
            };

            _mockConfigService.Setup(c => c.AddTenantAsync("newtenant", It.IsAny<TenantInfo>()))
                .ReturnsAsync(true);

            // Act
            IActionResult result = await _controller.CreateTenant(request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(CreatedAtActionResult));
            CreatedAtActionResult createdResult = (CreatedAtActionResult)result;
            Assert.IsNotNull(createdResult.Value);

            _mockConfigService.Verify(c => c.AddTenantAsync("newtenant", It.IsAny<TenantInfo>()), Times.Once);
        }

        [TestMethod]
        public async Task CreateTenant_WhenUserIsNotAdmin_ReturnsForbid()
        {
            // Arrange
            SetupNonAdminUser();
            CreateTenantRequest request = new CreateTenantRequest
            {
                TenantId = "newtenant",
                ApiKey = "new-key",
                DisplayName = "New Tenant",
                StorageLimitBytes = 1024 * 1024 * 200,
                IsAdmin = false
            };

            // Act
            IActionResult result = await _controller.CreateTenant(request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ForbidResult));
        }

        [TestMethod]
        public async Task CreateTenant_WithMissingTenantId_ReturnsBadRequest()
        {
            // Arrange
            SetupAdminUser();
            CreateTenantRequest request = new CreateTenantRequest
            {
                TenantId = "",
                ApiKey = "new-key",
                DisplayName = "New Tenant",
                StorageLimitBytes = 1024 * 1024 * 200,
                IsAdmin = false
            };

            // Act
            IActionResult result = await _controller.CreateTenant(request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            BadRequestObjectResult badRequest = (BadRequestObjectResult)result;
            Assert.AreEqual("Tenant ID is required", badRequest.Value);
        }

        [TestMethod]
        public async Task CreateTenant_WithMissingApiKey_ReturnsBadRequest()
        {
            // Arrange
            SetupAdminUser();
            CreateTenantRequest request = new CreateTenantRequest
            {
                TenantId = "newtenant",
                ApiKey = "",
                DisplayName = "New Tenant",
                StorageLimitBytes = 1024 * 1024 * 200,
                IsAdmin = false
            };

            // Act
            IActionResult result = await _controller.CreateTenant(request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            BadRequestObjectResult badRequest = (BadRequestObjectResult)result;
            Assert.AreEqual("API key is required", badRequest.Value);
        }

        [TestMethod]
        public async Task CreateTenant_WithMissingDisplayName_ReturnsBadRequest()
        {
            // Arrange
            SetupAdminUser();
            CreateTenantRequest request = new CreateTenantRequest
            {
                TenantId = "newtenant",
                ApiKey = "new-key",
                DisplayName = "",
                StorageLimitBytes = 1024 * 1024 * 200,
                IsAdmin = false
            };

            // Act
            IActionResult result = await _controller.CreateTenant(request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            BadRequestObjectResult badRequest = (BadRequestObjectResult)result;
            Assert.AreEqual("Display name is required", badRequest.Value);
        }

        [TestMethod]
        public async Task CreateTenant_WithInvalidStorageLimit_ReturnsBadRequest()
        {
            // Arrange
            SetupAdminUser();
            CreateTenantRequest request = new CreateTenantRequest
            {
                TenantId = "newtenant",
                ApiKey = "new-key",
                DisplayName = "New Tenant",
                StorageLimitBytes = 0, // Invalid for non-admin
                IsAdmin = false
            };

            // Act
            IActionResult result = await _controller.CreateTenant(request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            BadRequestObjectResult badRequest = (BadRequestObjectResult)result;
            Assert.AreEqual("Storage limit must be greater than 0 for non-admin tenants", badRequest.Value);
        }

        [TestMethod]
        public async Task CreateTenant_WhenTenantAlreadyExists_ReturnsConflict()
        {
            // Arrange
            SetupAdminUser();
            CreateTenantRequest request = new CreateTenantRequest
            {
                TenantId = "tenant1", // Already exists
                ApiKey = "new-key",
                DisplayName = "New Tenant",
                StorageLimitBytes = 1024 * 1024 * 200,
                IsAdmin = false
            };

            _mockConfigService.Setup(c => c.AddTenantAsync("tenant1", It.IsAny<TenantInfo>()))
                .ReturnsAsync(false);

            // Act
            IActionResult result = await _controller.CreateTenant(request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ConflictObjectResult));
            ConflictObjectResult conflictResult = (ConflictObjectResult)result;
            Assert.AreEqual("A tenant with the specified ID already exists", conflictResult.Value);
        }

        [TestMethod]
        public async Task UpdateTenantStorageLimit_WhenUserIsAdminAndTenantExists_UpdatesLimit()
        {
            // Arrange
            SetupAdminUser();
            UpdateStorageLimitRequest request = new UpdateStorageLimitRequest
            {
                StorageLimitBytes = 1024 * 1024 * 300 // 300MB
            };

            _mockConfigService.Setup(c => c.UpdateTenantAsync("tenant1", It.IsAny<TenantInfo>()))
                .ReturnsAsync(true);

            // Act
            IActionResult result = await _controller.UpdateTenantStorageLimit("tenant1", request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            _mockConfigService.Verify(c => c.UpdateTenantAsync("tenant1", It.IsAny<TenantInfo>()), Times.Once);
        }

        [TestMethod]
        public async Task UpdateTenantStorageLimit_WhenUserIsNotAdmin_ReturnsForbid()
        {
            // Arrange
            SetupNonAdminUser();
            UpdateStorageLimitRequest request = new UpdateStorageLimitRequest
            {
                StorageLimitBytes = 1024 * 1024 * 300
            };

            // Act
            IActionResult result = await _controller.UpdateTenantStorageLimit("tenant1", request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ForbidResult));
        }

        [TestMethod]
        public async Task UpdateTenantStorageLimit_WithInvalidStorageLimit_ReturnsBadRequest()
        {
            // Arrange
            SetupAdminUser();
            UpdateStorageLimitRequest request = new UpdateStorageLimitRequest
            {
                StorageLimitBytes = -1
            };

            // Act
            IActionResult result = await _controller.UpdateTenantStorageLimit("tenant1", request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            BadRequestObjectResult badRequest = (BadRequestObjectResult)result;
            Assert.AreEqual("Storage limit must be greater than 0", badRequest.Value);
        }

        [TestMethod]
        public async Task UpdateTenantStorageLimit_WhenTenantDoesNotExist_ReturnsNotFound()
        {
            // Arrange
            SetupAdminUser();
            UpdateStorageLimitRequest request = new UpdateStorageLimitRequest
            {
                StorageLimitBytes = 1024 * 1024 * 300
            };

            _mockConfigService.Setup(c => c.UpdateTenantAsync("nonexistent", It.IsAny<TenantInfo>()))
                .ReturnsAsync(false);

            // Act
            IActionResult result = await _controller.UpdateTenantStorageLimit("nonexistent", request, CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(NotFoundObjectResult));
            NotFoundObjectResult notFoundResult = (NotFoundObjectResult)result;
            Assert.AreEqual("Tenant not found", notFoundResult.Value);
        }

        [TestMethod]
        public async Task DeleteTenant_WhenUserIsAdminAndTenantExists_DeletesTenant()
        {
            // Arrange
            SetupAdminUser();

            _mockConfigService.Setup(c => c.RemoveTenantAsync("tenant1"))
                .ReturnsAsync(true);

            // Act
            IActionResult result = await _controller.DeleteTenant("tenant1", CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(NoContentResult));
            _mockConfigService.Verify(c => c.RemoveTenantAsync("tenant1"), Times.Once);
        }

        [TestMethod]
        public async Task DeleteTenant_WhenUserIsNotAdmin_ReturnsForbid()
        {
            // Arrange
            SetupNonAdminUser();

            // Act
            IActionResult result = await _controller.DeleteTenant("tenant1", CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ForbidResult));
        }

        [TestMethod]
        public async Task DeleteTenant_WhenTenantDoesNotExist_ReturnsNotFound()
        {
            // Arrange
            SetupAdminUser();

            _mockConfigService.Setup(c => c.RemoveTenantAsync("nonexistent"))
                .ReturnsAsync(false);

            // Act
            IActionResult result = await _controller.DeleteTenant("nonexistent", CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(NotFoundObjectResult));
            NotFoundObjectResult notFoundResult = (NotFoundObjectResult)result;
            Assert.AreEqual("Tenant not found", notFoundResult.Value);
        }

        private void SetupAdminUser()
        {
            _mockHttpContext.Object.Items["TenantId"] = "admin";
            _mockHttpContext.Object.Items["IsAdmin"] = true;
        }

        private void SetupNonAdminUser()
        {
            _mockHttpContext.Object.Items["TenantId"] = "user";
            _mockHttpContext.Object.Items["IsAdmin"] = false;
        }

        [TestMethod]
        public async Task GetTenants_UsesTotalUsageIncludingSubTenants()
        {
            // Arrange
            SetupAdminUser();
            long totalUsageIncludingSubTenants = 1024 * 1024 * 45; // 45MB total (including subtenants)

            _mockStorageService.Setup(s => s.GetTotalUsageIncludingSubTenants("tenant1")).Returns(totalUsageIncludingSubTenants);

            // Act
            IActionResult result = await _controller.GetTenants(CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            OkObjectResult okResult = (OkObjectResult)result;
            Assert.IsNotNull(okResult.Value);

            // Verify that GetTotalUsageIncludingSubTenants was called
            _mockStorageService.Verify(s => s.GetTotalUsageIncludingSubTenants("tenant1"), Times.Once);
        }

        [TestMethod]
        public async Task GetTenant_UsesTotalUsageIncludingSubTenants()
        {
            // Arrange
            SetupAdminUser();
            long totalUsageIncludingSubTenants = 1024 * 1024 * 40; // 40MB total (including subtenants)

            _mockStorageService.Setup(s => s.GetTotalUsageIncludingSubTenants("tenant1")).Returns(totalUsageIncludingSubTenants);

            // Act
            IActionResult result = await _controller.GetTenant("tenant1", CancellationToken.None);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            OkObjectResult okResult = (OkObjectResult)result;
            Assert.IsNotNull(okResult.Value);

            // Verify that GetTotalUsageIncludingSubTenants was called
            _mockStorageService.Verify(s => s.GetTotalUsageIncludingSubTenants("tenant1"), Times.Once);
        }
    }
}