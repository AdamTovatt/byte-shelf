using ByteShelf.Configuration;
using ByteShelf.Services;
using ByteShelfCommon;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Text.Json;

namespace ByteShelf.Tests
{
    [TestClass]
    public class TenantStorageServiceTests
    {
        private string _tempStoragePath = null!;
        private Mock<ITenantConfigurationService> _mockConfigService = null!;
        private Mock<IConfiguration> _mockConfiguration = null!;
        private TestLogger<TenantStorageService> _logger = null!;
        private TenantStorageService _service = null!;
        private TenantConfiguration _tenantConfig = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempStoragePath = Path.Combine(Path.GetTempPath(), $"ByteShelf-TenantStorage-Test-{Guid.NewGuid()}");
            _mockConfigService = new Mock<ITenantConfigurationService>();
            _mockConfiguration = new Mock<IConfiguration>();
            _logger = new TestLogger<TenantStorageService>();

            // Setup configuration
            _mockConfiguration.Setup(c => c["StoragePath"]).Returns(_tempStoragePath);

            // Setup tenant configuration
            _tenantConfig = new TenantConfiguration
            {
                RequireAuthentication = true,
                Tenants = new Dictionary<string, TenantInfo>
                {
                    ["tenant1"] = new TenantInfo
                    {
                        ApiKey = "tenant1-key",
                        DisplayName = "Tenant 1",
                        StorageLimitBytes = 1024 * 1024 * 100, // 100MB
                        IsAdmin = false
                    },
                    ["admin"] = new TenantInfo
                    {
                        ApiKey = "admin-key",
                        DisplayName = "Admin",
                        StorageLimitBytes = 0, // Unlimited
                        IsAdmin = true
                    }
                }
            };

            _mockConfigService.Setup(c => c.GetConfiguration()).Returns(_tenantConfig);

            _service = new TenantStorageService(_mockConfigService.Object, _logger, _mockConfiguration.Object);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempStoragePath))
            {
                Directory.Delete(_tempStoragePath, true);
            }
        }

        [TestMethod]
        public void Constructor_InitializesServiceCorrectly()
        {
            // Assert
            Assert.IsNotNull(_service);
            Assert.IsTrue(Directory.Exists(_tempStoragePath));
        }

        [TestMethod]
        public void Constructor_ThrowsArgumentNullException_WhenConfigServiceIsNull()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new TenantStorageService(null!, _logger, _mockConfiguration.Object));
        }

        [TestMethod]
        public void Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new TenantStorageService(_mockConfigService.Object, null!, _mockConfiguration.Object));
        }

        [TestMethod]
        public void Constructor_ThrowsArgumentNullException_WhenConfigurationIsNull()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new TenantStorageService(_mockConfigService.Object, _logger, null!));
        }

        [TestMethod]
        public void CanStoreData_ReturnsTrue_WhenTenantExistsAndHasSpace()
        {
            // Arrange
            string tenantId = "tenant1";
            long fileSize = 1024 * 1024 * 50; // 50MB

            // Act
            bool result = _service.CanStoreData(tenantId, fileSize);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void CanStoreData_ReturnsFalse_WhenTenantDoesNotExist()
        {
            // Arrange
            string tenantId = "nonexistent";
            long fileSize = 1024;

            // Act
            bool result = _service.CanStoreData(tenantId, fileSize);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void CanStoreData_ReturnsFalse_WhenTenantExceedsLimit()
        {
            // Arrange
            string tenantId = "tenant1";
            long fileSize = 1024 * 1024 * 150; // 150MB (exceeds 100MB limit)

            // Act
            bool result = _service.CanStoreData(tenantId, fileSize);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void CanStoreData_ReturnsTrue_WhenAdminHasUnlimitedStorage()
        {
            // Arrange
            string tenantId = "admin";
            long fileSize = 1024L * 1024L * 1024L * 10L; // 10GB

            // Act
            bool result = _service.CanStoreData(tenantId, fileSize);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void RecordStorageUsed_UpdatesUsageCorrectly()
        {
            // Arrange
            string tenantId = "tenant1";
            long initialUsage = _service.GetCurrentUsage(tenantId);
            long fileSize = 1024 * 1024; // 1MB

            // Act
            _service.RecordStorageUsed(tenantId, fileSize);

            // Assert
            long newUsage = _service.GetCurrentUsage(tenantId);
            Assert.AreEqual(initialUsage + fileSize, newUsage);
        }

        [TestMethod]
        public void RecordStorageFreed_UpdatesUsageCorrectly()
        {
            // Arrange
            string tenantId = "tenant1";
            _service.RecordStorageUsed(tenantId, 1024 * 1024 * 10); // Add 10MB
            long fileSize = 1024 * 1024 * 3; // Free 3MB

            // Act
            _service.RecordStorageFreed(tenantId, fileSize);

            // Assert
            long usage = _service.GetCurrentUsage(tenantId);
            Assert.AreEqual(1024 * 1024 * 7, usage); // Should be 7MB remaining
        }

        [TestMethod]
        public void RecordStorageFreed_DoesNotGoBelowZero()
        {
            // Arrange
            string tenantId = "tenant1";
            long fileSize = 1024 * 1024 * 10; // Try to free 10MB

            // Act
            _service.RecordStorageFreed(tenantId, fileSize);

            // Assert
            long usage = _service.GetCurrentUsage(tenantId);
            Assert.AreEqual(0, usage);
        }

        [TestMethod]
        public void GetCurrentUsage_ReturnsZero_WhenTenantHasNoUsage()
        {
            // Arrange
            string tenantId = "tenant1";

            // Act
            long usage = _service.GetCurrentUsage(tenantId);

            // Assert
            Assert.AreEqual(0, usage);
        }

        [TestMethod]
        public void GetStorageLimit_ReturnsCorrectLimit()
        {
            // Arrange
            string tenantId = "tenant1";

            // Act
            long limit = _service.GetStorageLimit(tenantId);

            // Assert
            Assert.AreEqual(1024 * 1024 * 100, limit); // 100MB
        }

        [TestMethod]
        public void GetStorageLimit_ReturnsZero_WhenTenantDoesNotExist()
        {
            // Arrange
            string tenantId = "nonexistent";

            // Act
            long limit = _service.GetStorageLimit(tenantId);

            // Assert
            Assert.AreEqual(0, limit);
        }

        [TestMethod]
        public void RebuildUsageCache_CalculatesUsageFromMetadataFiles()
        {
            // Arrange
            string tenantId = "tenant1";
            string tenantDir = Path.Combine(_tempStoragePath, tenantId);
            string metadataDir = Path.Combine(tenantDir, "metadata");
            Directory.CreateDirectory(metadataDir);

            // Create test metadata files
            CreateTestMetadataFile(metadataDir, "file1.json", 1024 * 1024 * 10); // 10MB
            CreateTestMetadataFile(metadataDir, "file2.json", 1024 * 1024 * 20); // 20MB

            // Act
            _service.RebuildUsageCache();

            // Assert
            long usage = _service.GetCurrentUsage(tenantId);
            Assert.AreEqual(1024 * 1024 * 30, usage); // Should be 30MB total
        }

        [TestMethod]
        public void RebuildUsageCache_HandlesMultipleTenants()
        {
            // Arrange
            string tenant1Dir = Path.Combine(_tempStoragePath, "tenant1");
            string tenant2Dir = Path.Combine(_tempStoragePath, "tenant2");
            string metadata1Dir = Path.Combine(tenant1Dir, "metadata");
            string metadata2Dir = Path.Combine(tenant2Dir, "metadata");

            Directory.CreateDirectory(metadata1Dir);
            Directory.CreateDirectory(metadata2Dir);

            CreateTestMetadataFile(metadata1Dir, "file1.json", 1024 * 1024 * 10); // 10MB
            CreateTestMetadataFile(metadata2Dir, "file2.json", 1024 * 1024 * 15); // 15MB

            // Act
            _service.RebuildUsageCache();

            // Assert
            long usage1 = _service.GetCurrentUsage("tenant1");
            long usage2 = _service.GetCurrentUsage("tenant2");
            Assert.AreEqual(1024 * 1024 * 10, usage1);
            Assert.AreEqual(1024 * 1024 * 15, usage2);
        }

        [TestMethod]
        public void RebuildUsageCache_HandlesCorruptedMetadataFiles()
        {
            // Arrange
            string tenantId = "tenant1";
            string tenantDir = Path.Combine(_tempStoragePath, tenantId);
            string metadataDir = Path.Combine(tenantDir, "metadata");
            Directory.CreateDirectory(metadataDir);

            // Create valid metadata file
            CreateTestMetadataFile(metadataDir, "valid.json", 1024 * 1024 * 10);

            // Create corrupted metadata file
            File.WriteAllText(Path.Combine(metadataDir, "corrupted.json"), "invalid json content");

            // Act
            _service.RebuildUsageCache();

            // Assert
            long usage = _service.GetCurrentUsage(tenantId);
            Assert.AreEqual(1024 * 1024 * 10, usage); // Should only count the valid file
        }

        [TestMethod]
        public void RebuildUsageCache_HandlesEmptyTenantDirectories()
        {
            // Arrange
            string tenantId = "tenant1";
            string tenantDir = Path.Combine(_tempStoragePath, tenantId);
            Directory.CreateDirectory(tenantDir);
            // Don't create metadata directory

            // Act
            _service.RebuildUsageCache();

            // Assert
            long usage = _service.GetCurrentUsage(tenantId);
            Assert.AreEqual(0, usage);
        }

        [TestMethod]
        public void RebuildUsageCache_PersistsResultsToDisk()
        {
            // Arrange
            string tenantId = "tenant1";
            string tenantDir = Path.Combine(_tempStoragePath, tenantId);
            string metadataDir = Path.Combine(tenantDir, "metadata");
            Directory.CreateDirectory(metadataDir);

            CreateTestMetadataFile(metadataDir, "file1.json", 1024 * 1024 * 25);

            // Act
            _service.RebuildUsageCache();

            // Assert
            string usageFilePath = Path.Combine(_tempStoragePath, "usage.json");
            Assert.IsTrue(File.Exists(usageFilePath));

            string json = File.ReadAllText(usageFilePath);
            Dictionary<string, long>? usageData = JsonSerializer.Deserialize<Dictionary<string, long>>(json);
            Assert.IsNotNull(usageData);
            Assert.IsTrue(usageData.ContainsKey(tenantId));
            Assert.AreEqual(1024 * 1024 * 25, usageData[tenantId]);
        }

        [TestMethod]
        public void Startup_LoadsAndRebuildsUsageData()
        {
            // Arrange
            string tenantId = "tenant1";
            string tenantDir = Path.Combine(_tempStoragePath, tenantId);
            string metadataDir = Path.Combine(tenantDir, "metadata");
            Directory.CreateDirectory(metadataDir);

            CreateTestMetadataFile(metadataDir, "file1.json", 1024 * 1024 * 30);

            // Act - Create a new service instance to trigger startup rebuild
            TenantStorageService newService = new TenantStorageService(
                _mockConfigService.Object, _logger, _mockConfiguration.Object);

            // Assert
            long usage = newService.GetCurrentUsage(tenantId);
            Assert.AreEqual(1024 * 1024 * 30, usage);
        }

        [TestMethod]
        public void Persistence_WorksCorrectly()
        {
            // Arrange
            string tenantId = "tenant1";
            _service.RecordStorageUsed(tenantId, 1024 * 1024 * 50);

            // Force persistence by doing enough operations to trigger automatic persistence (every 10 operations)
            for (int i = 0; i < 9; i++) // Do 9 more operations to reach 10 total
            {
                _service.RecordStorageUsed(tenantId, 0); // Add 0 bytes to trigger operation count
            }

            // Verify the usage.json file was created
            string usageFilePath = Path.Combine(_tempStoragePath, "usage.json");
            Assert.IsTrue(File.Exists(usageFilePath));

            // Act - Create a new service instance to test persistence
            TenantStorageService newService = new TenantStorageService(
                _mockConfigService.Object, _logger, _mockConfiguration.Object);

            // Assert
            long usage = newService.GetCurrentUsage(tenantId);
            Assert.AreEqual(1024 * 1024 * 50, usage);
        }

        private void CreateTestMetadataFile(string metadataDir, string filename, long fileSize)
        {
            ShelfFileMetadata metadata = new ShelfFileMetadata(
                Guid.NewGuid(),
                filename,
                "application/octet-stream",
                fileSize,
                new List<Guid> { Guid.NewGuid() });

            string json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(metadataDir, filename), json);
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