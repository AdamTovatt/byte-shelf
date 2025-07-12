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
        private TestLogger<StorageService> _logger = null!;
        private StorageService _service = null!;
        private TenantConfiguration _tenantConfig = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempStoragePath = Path.Combine(Path.GetTempPath(), $"ByteShelf-TenantStorage-Test-{Guid.NewGuid()}");
            _mockConfigService = new Mock<ITenantConfigurationService>();
            _mockConfiguration = new Mock<IConfiguration>();
            _logger = new TestLogger<StorageService>();

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

            _service = new StorageService(_mockConfigService.Object, _logger, _mockConfiguration.Object);
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
                new StorageService(null!, _logger, _mockConfiguration.Object));
        }

        [TestMethod]
        public void Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new StorageService(_mockConfigService.Object, null!, _mockConfiguration.Object));
        }

        [TestMethod]
        public void Constructor_ThrowsArgumentNullException_WhenConfigurationIsNull()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
                new StorageService(_mockConfigService.Object, _logger, null!));
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
        public void CanStoreData_WithSharedStorage_WhenParentAndSubTenantsShareQuota()
        {
            // Arrange - Parent has 500MB, two subtenants share this quota
            TenantConfiguration configWithSubTenants = new TenantConfiguration
            {
                RequireAuthentication = true,
                Tenants = new Dictionary<string, TenantInfo>
                {
                    ["parent"] = new TenantInfo
                    {
                        ApiKey = "parent-key",
                        DisplayName = "Parent Tenant",
                        StorageLimitBytes = 500 * 1024 * 1024, // 500MB
                        IsAdmin = false,
                        SubTenants = new Dictionary<string, TenantInfo>
                        {
                            ["child1"] = new TenantInfo
                            {
                                ApiKey = "child1-key",
                                DisplayName = "Child 1",
                                StorageLimitBytes = 500 * 1024 * 1024, // Inherits parent's 500MB
                                IsAdmin = false,
                                SubTenants = new Dictionary<string, TenantInfo>()
                            },
                            ["child2"] = new TenantInfo
                            {
                                ApiKey = "child2-key",
                                DisplayName = "Child 2",
                                StorageLimitBytes = 500 * 1024 * 1024, // Inherits parent's 500MB
                                IsAdmin = false,
                                SubTenants = new Dictionary<string, TenantInfo>()
                            }
                        }
                    }
                }
            };

            configWithSubTenants.Tenants["parent"].SubTenants["child1"].Parent = configWithSubTenants.Tenants["parent"];
            configWithSubTenants.Tenants["parent"].SubTenants["child2"].Parent = configWithSubTenants.Tenants["parent"];

            _mockConfigService.Setup(c => c.GetConfiguration()).Returns(configWithSubTenants);

            // Act & Assert - Initially all tenants can store up to 500MB
            Assert.IsTrue(_service.CanStoreData("parent", 500 * 1024 * 1024)); // Parent can use full 500MB
            Assert.IsTrue(_service.CanStoreData("child1", 500 * 1024 * 1024)); // Child1 can use full 500MB
            Assert.IsTrue(_service.CanStoreData("child2", 500 * 1024 * 1024)); // Child2 can use full 500MB

            // Child1 uses 400MB
            _service.RecordStorageUsed("child1", 400 * 1024 * 1024);

            // Now only 100MB should be available for everyone
            Assert.IsFalse(_service.CanStoreData("parent", 200 * 1024 * 1024)); // Parent cannot use 200MB
            Assert.IsTrue(_service.CanStoreData("parent", 100 * 1024 * 1024)); // Parent can use 100MB
            Assert.IsFalse(_service.CanStoreData("child1", 200 * 1024 * 1024)); // Child1 cannot use 200MB more
            Assert.IsTrue(_service.CanStoreData("child1", 100 * 1024 * 1024)); // Child1 can use 100MB more
            Assert.IsFalse(_service.CanStoreData("child2", 200 * 1024 * 1024)); // Child2 cannot use 200MB
            Assert.IsTrue(_service.CanStoreData("child2", 100 * 1024 * 1024)); // Child2 can use 100MB

            // Child2 uses the remaining 100MB
            _service.RecordStorageUsed("child2", 100 * 1024 * 1024);

            // Now no one should be able to store anything
            Assert.IsFalse(_service.CanStoreData("parent", 1 * 1024 * 1024)); // Parent cannot use 1MB
            Assert.IsFalse(_service.CanStoreData("child1", 1 * 1024 * 1024)); // Child1 cannot use 1MB
            Assert.IsFalse(_service.CanStoreData("child2", 1 * 1024 * 1024)); // Child2 cannot use 1MB
        }

        [TestMethod]
        public void CanStoreData_WithSharedStorage_WhenParentAlsoUsesStorage()
        {
            // Arrange - Parent has 500MB, parent and subtenants share this quota
            TenantConfiguration configWithSubTenants = new TenantConfiguration
            {
                RequireAuthentication = true,
                Tenants = new Dictionary<string, TenantInfo>
                {
                    ["parent"] = new TenantInfo
                    {
                        ApiKey = "parent-key",
                        DisplayName = "Parent Tenant",
                        StorageLimitBytes = 500 * 1024 * 1024, // 500MB
                        IsAdmin = false,
                        SubTenants = new Dictionary<string, TenantInfo>
                        {
                            ["child"] = new TenantInfo
                            {
                                ApiKey = "child-key",
                                DisplayName = "Child",
                                StorageLimitBytes = 500 * 1024 * 1024, // Inherits parent's 500MB
                                IsAdmin = false,
                                SubTenants = new Dictionary<string, TenantInfo>()
                            }
                        }
                    }
                }
            };

            configWithSubTenants.Tenants["parent"].SubTenants["child"].Parent = configWithSubTenants.Tenants["parent"];

            _mockConfigService.Setup(c => c.GetConfiguration()).Returns(configWithSubTenants);

            // Parent uses 300MB
            _service.RecordStorageUsed("parent", 300 * 1024 * 1024);

            // Child should only be able to use 200MB
            Assert.IsFalse(_service.CanStoreData("child", 250 * 1024 * 1024)); // Child cannot use 250MB
            Assert.IsTrue(_service.CanStoreData("child", 200 * 1024 * 1024)); // Child can use 200MB

            // Parent should only be able to use 200MB more
            Assert.IsFalse(_service.CanStoreData("parent", 250 * 1024 * 1024)); // Parent cannot use 250MB more
            Assert.IsTrue(_service.CanStoreData("parent", 200 * 1024 * 1024)); // Parent can use 200MB more
        }

        [TestMethod]
        public void CanStoreData_WithSharedStorage_WhenSubTenantFreesStorage()
        {
            // Arrange - Parent has 500MB, two subtenants share this quota
            TenantConfiguration configWithSubTenants = new TenantConfiguration
            {
                RequireAuthentication = true,
                Tenants = new Dictionary<string, TenantInfo>
                {
                    ["parent"] = new TenantInfo
                    {
                        ApiKey = "parent-key",
                        DisplayName = "Parent Tenant",
                        StorageLimitBytes = 500 * 1024 * 1024, // 500MB
                        IsAdmin = false,
                        SubTenants = new Dictionary<string, TenantInfo>
                        {
                            ["child1"] = new TenantInfo
                            {
                                ApiKey = "child1-key",
                                DisplayName = "Child 1",
                                StorageLimitBytes = 500 * 1024 * 1024, // Inherits parent's 500MB
                                IsAdmin = false,
                                SubTenants = new Dictionary<string, TenantInfo>()
                            },
                            ["child2"] = new TenantInfo
                            {
                                ApiKey = "child2-key",
                                DisplayName = "Child 2",
                                StorageLimitBytes = 500 * 1024 * 1024, // Inherits parent's 500MB
                                IsAdmin = false,
                                SubTenants = new Dictionary<string, TenantInfo>()
                            }
                        }
                    }
                }
            };

            configWithSubTenants.Tenants["parent"].SubTenants["child1"].Parent = configWithSubTenants.Tenants["parent"];
            configWithSubTenants.Tenants["parent"].SubTenants["child2"].Parent = configWithSubTenants.Tenants["parent"];

            _mockConfigService.Setup(c => c.GetConfiguration()).Returns(configWithSubTenants);

            // Child1 uses 400MB, Child2 uses 100MB
            _service.RecordStorageUsed("child1", 400 * 1024 * 1024);
            _service.RecordStorageUsed("child2", 100 * 1024 * 1024);

            // No one should be able to store anything
            Assert.IsFalse(_service.CanStoreData("parent", 1 * 1024 * 1024));
            Assert.IsFalse(_service.CanStoreData("child1", 1 * 1024 * 1024));
            Assert.IsFalse(_service.CanStoreData("child2", 1 * 1024 * 1024));

            // Child1 frees 200MB
            _service.RecordStorageFreed("child1", 200 * 1024 * 1024);

            // Now everyone should be able to use 200MB
            Assert.IsTrue(_service.CanStoreData("parent", 200 * 1024 * 1024));
            Assert.IsTrue(_service.CanStoreData("child1", 200 * 1024 * 1024));
            Assert.IsTrue(_service.CanStoreData("child2", 200 * 1024 * 1024));
        }

        [TestMethod]
        public void CanStoreData_WithSharedStorage_WhenParentHasUnlimitedStorage()
        {
            // Arrange - Parent has unlimited storage (0), subtenants inherit unlimited
            TenantConfiguration configWithSubTenants = new TenantConfiguration
            {
                RequireAuthentication = true,
                Tenants = new Dictionary<string, TenantInfo>
                {
                    ["parent"] = new TenantInfo
                    {
                        ApiKey = "parent-key",
                        DisplayName = "Parent Tenant",
                        StorageLimitBytes = 0, // Unlimited
                        IsAdmin = true,
                        SubTenants = new Dictionary<string, TenantInfo>
                        {
                            ["child"] = new TenantInfo
                            {
                                ApiKey = "child-key",
                                DisplayName = "Child",
                                StorageLimitBytes = 0, // Inherits parent's unlimited
                                IsAdmin = false,
                                SubTenants = new Dictionary<string, TenantInfo>()
                            }
                        }
                    }
                }
            };

            configWithSubTenants.Tenants["parent"].SubTenants["child"].Parent = configWithSubTenants.Tenants["parent"];

            _mockConfigService.Setup(c => c.GetConfiguration()).Returns(configWithSubTenants);

            // Both parent and child should be able to store unlimited amounts
            Assert.IsTrue(_service.CanStoreData("parent", 1024L * 1024L * 1024L * 10L)); // 10GB
            Assert.IsTrue(_service.CanStoreData("child", 1024L * 1024L * 1024L * 10L)); // 10GB

            // Even after using storage, they should still be unlimited
            _service.RecordStorageUsed("parent", 1024L * 1024L * 1024L * 5L); // 5GB
            _service.RecordStorageUsed("child", 1024L * 1024L * 1024L * 5L); // 5GB

            Assert.IsTrue(_service.CanStoreData("parent", 1024L * 1024L * 1024L * 10L)); // 10GB more
            Assert.IsTrue(_service.CanStoreData("child", 1024L * 1024L * 1024L * 10L)); // 10GB more
        }

        [TestMethod]
        public void CanStoreData_WithSharedStorage_WhenSubTenantHasLowerLimitThanParent()
        {
            // Arrange - Parent has 500MB, subtenant has 200MB (lower than parent)
            TenantConfiguration configWithSubTenants = new TenantConfiguration
            {
                RequireAuthentication = true,
                Tenants = new Dictionary<string, TenantInfo>
                {
                    ["parent"] = new TenantInfo
                    {
                        ApiKey = "parent-key",
                        DisplayName = "Parent Tenant",
                        StorageLimitBytes = 500 * 1024 * 1024, // 500MB
                        IsAdmin = false,
                        SubTenants = new Dictionary<string, TenantInfo>
                        {
                            ["child"] = new TenantInfo
                            {
                                ApiKey = "child-key",
                                DisplayName = "Child",
                                StorageLimitBytes = 200 * 1024 * 1024, // 200MB (lower than parent)
                                IsAdmin = false,
                                SubTenants = new Dictionary<string, TenantInfo>(),
                            }
                        }
                    }
                }
            };

            configWithSubTenants.Tenants["parent"].SubTenants["child"].Parent = configWithSubTenants.Tenants["parent"];

            _mockConfigService.Setup(c => c.GetConfiguration()).Returns(configWithSubTenants);

            // Child should be limited by its own limit (200MB), not parent's limit (500MB)
            Assert.IsTrue(_service.CanStoreData("child", 200 * 1024 * 1024)); // Child can use 200MB
            Assert.IsFalse(_service.CanStoreData("child", 250 * 1024 * 1024)); // Child cannot use 250MB

            // Parent should be limited by its own limit (500MB)
            Assert.IsTrue(_service.CanStoreData("parent", 500 * 1024 * 1024)); // Parent can use 500MB
            Assert.IsFalse(_service.CanStoreData("parent", 600 * 1024 * 1024)); // Parent cannot use 600MB

            // Child uses 150MB
            _service.RecordStorageUsed("child", 150 * 1024 * 1024);

            // Child should only be able to use 50MB more (200MB - 150MB = 50MB)
            Assert.IsTrue(_service.CanStoreData("child", 50 * 1024 * 1024)); // Child can use 50MB
            Assert.IsFalse(_service.CanStoreData("child", 60 * 1024 * 1024)); // Child cannot use 60MB

            // Parent should not still be able to use 500MB (it's affected by child's usage)
            Assert.IsFalse(_service.CanStoreData("parent", 500 * 1024 * 1024)); // Parent can use 500MB
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
            StorageService newService = new StorageService(
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
            StorageService newService = new StorageService(
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