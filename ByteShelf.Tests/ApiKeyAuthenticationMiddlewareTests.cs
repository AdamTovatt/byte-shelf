using ByteShelf.Configuration;
using ByteShelf.Middleware;
using ByteShelf.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Net;

namespace ByteShelf.Tests
{
    [TestClass]
    public class ApiKeyAuthenticationMiddlewareTests
    {
        private Mock<ITenantConfigurationService> _mockConfigService = null!;
        private TenantConfiguration _tenantConfig = null!;
        private ApiKeyAuthenticationMiddleware _middleware = null!;
        private Mock<RequestDelegate> _mockNext = null!;

        [TestInitialize]
        public void Setup()
        {
            _mockConfigService = new Mock<ITenantConfigurationService>();
            _mockNext = new Mock<RequestDelegate>();

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
                        StorageLimitBytes = 0,
                        IsAdmin = true,
                    },
                    ["tenant1"] = new TenantInfo
                    {
                        ApiKey = "tenant1-key",
                        DisplayName = "Tenant 1",
                        StorageLimitBytes = 1024 * 1024 * 100,
                        IsAdmin = false,
                    },
                    ["tenant2"] = new TenantInfo
                    {
                        ApiKey = "tenant2-key",
                        DisplayName = "Tenant 2",
                        StorageLimitBytes = 1024 * 1024 * 50,
                        IsAdmin = false,
                    }
                }
            };

            _mockConfigService.Setup(c => c.GetConfiguration()).Returns(_tenantConfig);
            _mockNext.Setup(n => n(It.IsAny<HttpContext>())).Returns(Task.CompletedTask);

            _middleware = new ApiKeyAuthenticationMiddleware(_mockNext.Object, _mockConfigService.Object);
            ApiKeyAuthenticationMiddleware.FailedAttemptMillisecondDelay = 2;
        }

        [TestMethod]
        public void Constructor_WithNullNext_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new ApiKeyAuthenticationMiddleware(null!, _mockConfigService.Object));
        }

        [TestMethod]
        public void Constructor_WithNullConfigService_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new ApiKeyAuthenticationMiddleware(_mockNext.Object, null!));
        }

        [TestMethod]
        public async Task InvokeAsync_WithHealthCheckPath_SkipsAuthentication()
        {
            // Arrange
            HttpContext context = CreateHttpContext("/health");

            // Act
            await _middleware.InvokeAsync(context);

            // Assert
            _mockNext.Verify(n => n(context), Times.Once);
            Assert.AreEqual(200, context.Response.StatusCode); // Default status code
        }

        [TestMethod]
        public async Task InvokeAsync_WithSwaggerPath_SkipsAuthentication()
        {
            // Arrange
            HttpContext context = CreateHttpContext("/swagger");

            // Act
            await _middleware.InvokeAsync(context);

            // Assert
            _mockNext.Verify(n => n(context), Times.Once);
            Assert.AreEqual(200, context.Response.StatusCode);
        }

        [TestMethod]
        public async Task InvokeAsync_WithSwaggerUiPath_SkipsAuthentication()
        {
            // Arrange
            HttpContext context = CreateHttpContext("/swagger-ui");

            // Act
            await _middleware.InvokeAsync(context);

            // Assert
            _mockNext.Verify(n => n(context), Times.Once);
            Assert.AreEqual(200, context.Response.StatusCode);
        }

        [TestMethod]
        public async Task InvokeAsync_WhenAuthenticationNotRequired_CallsNext()
        {
            // Arrange
            _tenantConfig.RequireAuthentication = false;
            HttpContext context = CreateHttpContext("/api/files");

            // Act
            await _middleware.InvokeAsync(context);

            // Assert
            _mockNext.Verify(n => n(context), Times.Once);
            Assert.AreEqual(200, context.Response.StatusCode);
        }

        [TestMethod]
        public async Task InvokeAsync_WithValidApiKey_SetsTenantIdAndCallsNext()
        {
            // Arrange
            HttpContext context = CreateHttpContext("/api/files");
            context.Request.Headers["X-API-Key"] = "tenant1-key";

            // Act
            await _middleware.InvokeAsync(context);

            // Assert
            _mockNext.Verify(n => n(context), Times.Once);
            Assert.AreEqual(200, context.Response.StatusCode);
            Assert.AreEqual<string>("tenant1", (string)context.Items["TenantId"]!);
            Assert.AreEqual<bool>(false, (bool)context.Items["IsAdmin"]!);
            Assert.AreEqual<string>("tenant1", context.Request.Headers["X-Tenant-ID"]);
        }

        [TestMethod]
        public async Task InvokeAsync_WithAdminApiKey_SetsAdminStatus()
        {
            // Arrange
            HttpContext context = CreateHttpContext("/api/admin/tenants");
            context.Request.Headers["X-API-Key"] = "admin-key";

            // Act
            await _middleware.InvokeAsync(context);

            // Assert
            _mockNext.Verify(n => n(context), Times.Once);
            Assert.AreEqual(200, context.Response.StatusCode);
            Assert.AreEqual<string>("admin", (string)context.Items["TenantId"]!);
            Assert.AreEqual<bool>(true, (bool)context.Items["IsAdmin"]!);
        }

        [TestMethod]
        public async Task InvokeAsync_WithMissingApiKey_ReturnsUnauthorized()
        {
            // Arrange
            HttpContext context = CreateHttpContext("/api/files");

            // Act
            await _middleware.InvokeAsync(context);

            // Assert
            _mockNext.Verify(n => n(It.IsAny<HttpContext>()), Times.Never);
            Assert.AreEqual((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);
            Assert.AreEqual("application/json", context.Response.ContentType);

            // Verify error response
            context.Response.Body.Position = 0;
            using StreamReader reader = new StreamReader(context.Response.Body);
            string responseBody = await reader.ReadToEndAsync();
            Assert.IsTrue(responseBody.Contains("Invalid or missing API key"));
        }

        [TestMethod]
        public async Task InvokeAsync_WithEmptyApiKey_ReturnsUnauthorized()
        {
            // Arrange
            HttpContext context = CreateHttpContext("/api/files");
            context.Request.Headers["X-API-Key"] = "";

            // Act
            await _middleware.InvokeAsync(context);

            // Assert
            _mockNext.Verify(n => n(It.IsAny<HttpContext>()), Times.Never);
            Assert.AreEqual((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);
        }

        [TestMethod]
        public async Task InvokeAsync_WithInvalidApiKey_ReturnsUnauthorized()
        {
            // Arrange
            HttpContext context = CreateHttpContext("/api/files");
            context.Request.Headers["X-API-Key"] = "invalid-key";

            // Act
            await _middleware.InvokeAsync(context);

            // Assert
            _mockNext.Verify(n => n(It.IsAny<HttpContext>()), Times.Never);
            Assert.AreEqual((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);
        }

        [TestMethod]
        public async Task InvokeAsync_WithWhitespaceApiKey_ReturnsUnauthorized()
        {
            // Arrange
            HttpContext context = CreateHttpContext("/api/files");
            context.Request.Headers["X-API-Key"] = "   ";

            // Act
            await _middleware.InvokeAsync(context);

            // Assert
            _mockNext.Verify(n => n(It.IsAny<HttpContext>()), Times.Never);
            Assert.AreEqual((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);
        }

        [TestMethod]
        public async Task InvokeAsync_WithCaseSensitiveApiKey_RespectsCase()
        {
            // Arrange
            HttpContext context = CreateHttpContext("/api/files");
            context.Request.Headers["X-API-Key"] = "TENANT1-KEY"; // Different case

            // Act
            await _middleware.InvokeAsync(context);

            // Assert
            _mockNext.Verify(n => n(It.IsAny<HttpContext>()), Times.Never);
            Assert.AreEqual((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);
        }

        [TestMethod]
        public async Task InvokeAsync_WithMultipleApiKeyHeaders_UsesFirstOne()
        {
            // Arrange
            HttpContext context = CreateHttpContext("/api/files");
            context.Request.Headers["X-API-Key"] = new string[] { "invalid-key", "tenant1-key" };

            // Act
            await _middleware.InvokeAsync(context);

            // Assert
            _mockNext.Verify(n => n(It.IsAny<HttpContext>()), Times.Never);
            Assert.AreEqual((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);
        }

        [TestMethod]
        public async Task InvokeAsync_WithValidApiKeyAndDifferentPaths_SetsTenantIdCorrectly()
        {
            // Arrange
            string[] paths = { "/api/files", "/api/chunks", "/api/tenant/storage", "/api/admin/tenants" };

            foreach (string path in paths)
            {
                HttpContext context = CreateHttpContext(path);
                context.Request.Headers["X-API-Key"] = "tenant2-key";

                // Act
                await _middleware.InvokeAsync(context);

                // Assert
                _mockNext.Verify(n => n(context), Times.Once);
                Assert.AreEqual<string>("tenant2", (string)context.Items["TenantId"]!);
                Assert.AreEqual<bool>(false, (bool)context.Items["IsAdmin"]!);

                // Reset for next iteration
                _mockNext.Reset();
            }
        }

        [TestMethod]
        public async Task InvokeAsync_WithValidApiKey_SetsCorrectHeaders()
        {
            // Arrange
            HttpContext context = CreateHttpContext("/api/files");
            context.Request.Headers["X-API-Key"] = "tenant1-key";

            // Act
            await _middleware.InvokeAsync(context);

            // Assert
            Assert.AreEqual<string>("tenant1", context.Request.Headers["X-Tenant-ID"]);
        }

        [TestMethod]
        public async Task InvokeAsync_WithValidApiKey_DoesNotModifyOtherHeaders()
        {
            // Arrange
            HttpContext context = CreateHttpContext("/api/files");
            context.Request.Headers["X-API-Key"] = "tenant1-key";
            context.Request.Headers["User-Agent"] = "TestAgent";
            context.Request.Headers["Accept"] = "application/json";

            // Act
            await _middleware.InvokeAsync(context);

            // Assert
            Assert.AreEqual<string>("TestAgent", context.Request.Headers["User-Agent"]);
            Assert.AreEqual<string>("application/json", context.Request.Headers["Accept"]);
        }

        [TestMethod]
        public async Task InvokeAsync_WithExceptionInNext_PropagatesException()
        {
            // Arrange
            HttpContext context = CreateHttpContext("/api/files");
            context.Request.Headers["X-API-Key"] = "tenant1-key";

            Exception expectedException = new InvalidOperationException("Test exception");
            _mockNext.Setup(n => n(context)).Throws(expectedException);

            // Act & Assert
            Exception actualException = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _middleware.InvokeAsync(context));

            Assert.AreEqual(expectedException, actualException);
        }

        [TestMethod]
        public async Task InvokeAsync_WithExceptionInConfigService_PropagatesException()
        {
            // Arrange
            HttpContext context = CreateHttpContext("/api/files");
            context.Request.Headers["X-API-Key"] = "tenant1-key";

            Exception expectedException = new InvalidOperationException("Config service exception");
            _mockConfigService.Setup(c => c.GetConfiguration()).Throws(expectedException);

            // Act & Assert
            Exception actualException = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _middleware.InvokeAsync(context));

            Assert.AreEqual(expectedException, actualException);
        }

        private static HttpContext CreateHttpContext(string path)
        {
            HttpContext context = new DefaultHttpContext();
            context.Request.Path = path;
            context.Response.Body = new MemoryStream();
            return context;
        }
    }
}