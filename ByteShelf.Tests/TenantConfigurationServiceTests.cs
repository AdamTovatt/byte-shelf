using ByteShelf.Configuration;
using ByteShelf.Services;
using ByteShelfCommon;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace ByteShelf.Tests
{
    [TestClass]
    public class TenantConfigurationServiceTests
    {
        private string _testConfigPath = null!;
        private TestLogger<TenantConfigurationService> _logger = null!;
        private TenantConfigurationService _service = null!;

        [TestInitialize]
        public void Setup()
        {
            _testConfigPath = Path.GetTempFileName();
            _logger = new TestLogger<TenantConfigurationService>();

            // Set environment variable for test
            Environment.SetEnvironmentVariable("BYTESHELF_TENANT_CONFIG_PATH", _testConfigPath);

            _service = new TenantConfigurationService(_logger);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _service?.Dispose();

            if (File.Exists(_testConfigPath))
            {
                File.Delete(_testConfigPath);
            }

            Environment.SetEnvironmentVariable("BYTESHELF_TENANT_CONFIG_PATH", null);
        }

        [TestMethod]
        public void Constructor_CreatesDefaultConfiguration_WhenFileDoesNotExist()
        {
            // Arrange & Act
            TenantConfiguration config = _service.GetConfiguration();

            // Assert
            Assert.IsNotNull(config);
            Assert.IsTrue(config.RequireAuthentication);
            Assert.IsTrue(config.Tenants.ContainsKey("admin"));
            Assert.IsTrue(config.Tenants.ContainsKey("tenant1"));
            Assert.IsTrue(config.Tenants["admin"].IsAdmin);
            Assert.IsFalse(config.Tenants["tenant1"].IsAdmin);
        }

        [TestMethod]
        public void GetConfigurationFilePath_ReturnsCorrectPath()
        {
            // Act
            string path = _service.GetConfigurationFilePath();

            // Assert
            Assert.AreEqual(_testConfigPath, path);
        }

        [TestMethod]
        public async Task AddTenantAsync_AddsNewTenant_WhenTenantDoesNotExist()
        {
            // Arrange
            string tenantId = "newtenant";
            TenantInfo tenantInfo = new TenantInfo
            {
                ApiKey = "new-tenant-key",
                DisplayName = "New Tenant",
                StorageLimitBytes = 1024 * 1024 * 100, // 100MB
                IsAdmin = false
            };

            // Act
            bool result = await _service.AddTenantAsync(tenantId, tenantInfo);

            // Assert
            Assert.IsTrue(result);

            TenantConfiguration config = _service.GetConfiguration();
            Assert.IsTrue(config.Tenants.ContainsKey(tenantId));
            Assert.AreEqual(tenantInfo.ApiKey, config.Tenants[tenantId].ApiKey);
            Assert.AreEqual(tenantInfo.DisplayName, config.Tenants[tenantId].DisplayName);
            Assert.AreEqual(tenantInfo.StorageLimitBytes, config.Tenants[tenantId].StorageLimitBytes);
            Assert.AreEqual(tenantInfo.IsAdmin, config.Tenants[tenantId].IsAdmin);
        }

        [TestMethod]
        public async Task AddTenantAsync_ReturnsFalse_WhenTenantAlreadyExists()
        {
            // Arrange
            string tenantId = "admin"; // Already exists in default config
            TenantInfo tenantInfo = new TenantInfo
            {
                ApiKey = "duplicate-key",
                DisplayName = "Duplicate Tenant",
                StorageLimitBytes = 1024 * 1024 * 100,
                IsAdmin = false
            };

            // Act
            bool result = await _service.AddTenantAsync(tenantId, tenantInfo);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task UpdateTenantAsync_UpdatesExistingTenant()
        {
            // Arrange
            string tenantId = "tenant1";
            TenantInfo updatedInfo = new TenantInfo
            {
                ApiKey = "updated-key",
                DisplayName = "Updated Tenant",
                StorageLimitBytes = 1024 * 1024 * 200, // 200MB
                IsAdmin = true
            };

            // Act
            bool result = await _service.UpdateTenantAsync(tenantId, updatedInfo);

            // Assert
            Assert.IsTrue(result);

            TenantConfiguration config = _service.GetConfiguration();
            Assert.AreEqual(updatedInfo.ApiKey, config.Tenants[tenantId].ApiKey);
            Assert.AreEqual(updatedInfo.DisplayName, config.Tenants[tenantId].DisplayName);
            Assert.AreEqual(updatedInfo.StorageLimitBytes, config.Tenants[tenantId].StorageLimitBytes);
            Assert.AreEqual(updatedInfo.IsAdmin, config.Tenants[tenantId].IsAdmin);
        }

        [TestMethod]
        public async Task UpdateTenantAsync_ReturnsFalse_WhenTenantDoesNotExist()
        {
            // Arrange
            string tenantId = "nonexistent";
            TenantInfo tenantInfo = new TenantInfo
            {
                ApiKey = "some-key",
                DisplayName = "Non-existent Tenant",
                StorageLimitBytes = 1024 * 1024 * 100,
                IsAdmin = false
            };

            // Act
            bool result = await _service.UpdateTenantAsync(tenantId, tenantInfo);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task RemoveTenantAsync_RemovesExistingTenant()
        {
            // Arrange
            string tenantId = "tenant1";

            // Act
            bool result = await _service.RemoveTenantAsync(tenantId);

            // Assert
            Assert.IsTrue(result);

            TenantConfiguration config = _service.GetConfiguration();
            Assert.IsFalse(config.Tenants.ContainsKey(tenantId));
        }

        [TestMethod]
        public async Task RemoveTenantAsync_ReturnsFalse_WhenTenantDoesNotExist()
        {
            // Arrange
            string tenantId = "nonexistent";

            // Act
            bool result = await _service.RemoveTenantAsync(tenantId);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task ReloadConfigurationAsync_ReloadsFromFile()
        {
            // Arrange
            TenantConfiguration newConfig = new TenantConfiguration
            {
                RequireAuthentication = false,
                Tenants = new Dictionary<string, TenantInfo>
                {
                    ["reloaded"] = new TenantInfo
                    {
                        ApiKey = "reloaded-key",
                        DisplayName = "Reloaded Tenant",
                        StorageLimitBytes = 1024 * 1024 * 50,
                        IsAdmin = false
                    }
                }
            };

            string jsonContent = JsonSerializer.Serialize(newConfig, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_testConfigPath, jsonContent);

            // Act
            bool result = await _service.ReloadConfigurationAsync();

            // Assert
            Assert.IsTrue(result);

            TenantConfiguration config = _service.GetConfiguration();
            Assert.IsFalse(config.RequireAuthentication);
            Assert.IsTrue(config.Tenants.ContainsKey("reloaded"));
            Assert.IsFalse(config.Tenants.ContainsKey("admin"));
        }

        [TestMethod]
        public async Task ReloadConfigurationAsync_ReturnsFalse_WhenFileDoesNotExist()
        {
            // Arrange
            File.Delete(_testConfigPath);

            // Act
            bool result = await _service.ReloadConfigurationAsync();

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() => new TenantConfigurationService(null!));
        }

        [TestMethod]
        public async Task AddTenantAsync_ThrowsArgumentException_WhenTenantIdIsNull()
        {
            // Arrange
            TenantInfo tenantInfo = new TenantInfo
            {
                ApiKey = "some-key",
                DisplayName = "Test Tenant",
                StorageLimitBytes = 1024 * 1024 * 100,
                IsAdmin = false
            };

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => _service.AddTenantAsync(null!, tenantInfo));
        }

        [TestMethod]
        public async Task AddTenantAsync_ThrowsArgumentNullException_WhenTenantInfoIsNull()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => _service.AddTenantAsync("test", null!));
        }

        [TestMethod]
        public async Task UpdateTenantAsync_ThrowsArgumentException_WhenTenantIdIsNull()
        {
            // Arrange
            TenantInfo tenantInfo = new TenantInfo
            {
                ApiKey = "some-key",
                DisplayName = "Test Tenant",
                StorageLimitBytes = 1024 * 1024 * 100,
                IsAdmin = false
            };

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => _service.UpdateTenantAsync(null!, tenantInfo));
        }

        [TestMethod]
        public async Task UpdateTenantAsync_ThrowsArgumentNullException_WhenTenantInfoIsNull()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() => _service.UpdateTenantAsync("test", null!));
        }

        [TestMethod]
        public async Task RemoveTenantAsync_ThrowsArgumentException_WhenTenantIdIsNull()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => _service.RemoveTenantAsync(null!));
        }

        private class TestLogger<T> : ILogger<T>
        {
            public List<string> LogMessages { get; } = new List<string>();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                string message = formatter(state, exception);
                LogMessages.Add($"[{logLevel}] {message}");
            }
        }
    }
}