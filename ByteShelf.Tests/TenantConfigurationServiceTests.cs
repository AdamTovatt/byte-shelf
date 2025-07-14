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
        public async Task ReloadConfigurationAsync_RebuildsParentRelationships_WhenConfigurationHasSubTenants()
        {
            // Arrange
            TenantConfiguration newConfig = new TenantConfiguration
            {
                RequireAuthentication = true,
                Tenants = new Dictionary<string, TenantInfo>
                {
                    ["parent1"] = new TenantInfo
                    {
                        ApiKey = "parent1-key",
                        DisplayName = "Parent 1",
                        StorageLimitBytes = 1024 * 1024 * 100,
                        IsAdmin = false,
                        SubTenants = new Dictionary<string, TenantInfo>
                        {
                            ["child1"] = new TenantInfo
                            {
                                ApiKey = "child1-key",
                                DisplayName = "Child 1",
                                StorageLimitBytes = 1024 * 1024 * 50,
                                IsAdmin = false,
                                SubTenants = new Dictionary<string, TenantInfo>
                                {
                                    ["grandchild1"] = new TenantInfo
                                    {
                                        ApiKey = "grandchild1-key",
                                        DisplayName = "Grandchild 1",
                                        StorageLimitBytes = 1024 * 1024 * 25,
                                        IsAdmin = false,
                                        SubTenants = new Dictionary<string, TenantInfo>()
                                    }
                                }
                            }
                        }
                    },
                    ["parent2"] = new TenantInfo
                    {
                        ApiKey = "parent2-key",
                        DisplayName = "Parent 2",
                        StorageLimitBytes = 1024 * 1024 * 200,
                        IsAdmin = false,
                        SubTenants = new Dictionary<string, TenantInfo>()
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

            // Verify parent relationships are correctly set
            TenantInfo parent1 = config.Tenants["parent1"];
            TenantInfo parent2 = config.Tenants["parent2"];
            TenantInfo child1 = parent1.SubTenants["child1"];
            TenantInfo grandchild1 = child1.SubTenants["grandchild1"];

            // Root tenants should have no parent
            Assert.IsNull(parent1.Parent);
            Assert.IsNull(parent2.Parent);

            // Child tenants should have correct parent
            Assert.AreSame(parent1, child1.Parent);
            Assert.AreSame(child1, grandchild1.Parent);
        }

        [TestMethod]
        public void Constructor_RebuildsParentRelationships_WhenLoadingExistingConfigurationWithSubTenants()
        {
            // Arrange
            TenantConfiguration configWithSubTenants = new TenantConfiguration
            {
                RequireAuthentication = true,
                Tenants = new Dictionary<string, TenantInfo>
                {
                    ["root"] = new TenantInfo
                    {
                        ApiKey = "root-key",
                        DisplayName = "Root Tenant",
                        StorageLimitBytes = 1024 * 1024 * 100,
                        IsAdmin = false,
                        SubTenants = new Dictionary<string, TenantInfo>
                        {
                            ["sub1"] = new TenantInfo
                            {
                                ApiKey = "sub1-key",
                                DisplayName = "Sub Tenant 1",
                                StorageLimitBytes = 1024 * 1024 * 50,
                                IsAdmin = false,
                                SubTenants = new Dictionary<string, TenantInfo>()
                            },
                            ["sub2"] = new TenantInfo
                            {
                                ApiKey = "sub2-key",
                                DisplayName = "Sub Tenant 2",
                                StorageLimitBytes = 1024 * 1024 * 50,
                                IsAdmin = false,
                                SubTenants = new Dictionary<string, TenantInfo>()
                            }
                        }
                    }
                }
            };

            string jsonContent = JsonSerializer.Serialize(configWithSubTenants, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_testConfigPath, jsonContent);

            // Act - Create new service instance to trigger loading from file
            TenantConfigurationService newService = new TenantConfigurationService(_logger);

            // Assert
            TenantConfiguration loadedConfig = newService.GetConfiguration();

            TenantInfo root = loadedConfig.Tenants["root"];
            TenantInfo sub1 = root.SubTenants["sub1"];
            TenantInfo sub2 = root.SubTenants["sub2"];

            // Root tenant should have no parent
            Assert.IsNull(root.Parent);

            // Sub tenants should have root as parent
            Assert.AreSame(root, sub1.Parent);
            Assert.AreSame(root, sub2.Parent);

            newService.Dispose();
        }

        [TestMethod]
        public void Constructor_RebuildsParentRelationships_WhenCreatingDefaultConfiguration()
        {
            // Arrange & Act
            // The service constructor should create a default configuration and rebuild relationships

            // Assert
            TenantConfiguration config = _service.GetConfiguration();

            // Default configuration has admin and tenant1 as root tenants
            TenantInfo admin = config.Tenants["admin"];
            TenantInfo tenant1 = config.Tenants["tenant1"];

            // Root tenants should have no parent
            Assert.IsNull(admin.Parent);
            Assert.IsNull(tenant1.Parent);

            // Default tenants should have empty subtenants collections
            Assert.AreEqual(0, admin.SubTenants.Count);
            Assert.AreEqual(0, tenant1.SubTenants.Count);
        }

        [TestMethod]
        public async Task CreateSubTenantAsync_RebuildsParentRelationships_AfterSavingConfiguration()
        {
            // Arrange
            string parentTenantId = "admin";
            string displayName = "Test Subtenant";

            // Act
            string subTenantId = await _service.CreateSubTenantAsync(parentTenantId, displayName);

            // Assert
            TenantConfiguration config = _service.GetConfiguration();
            TenantInfo parent = config.Tenants[parentTenantId];
            TenantInfo subTenant = parent.SubTenants[subTenantId];

            // Verify parent relationship is correctly set
            Assert.AreSame(parent, subTenant.Parent);
            Assert.AreEqual(displayName, subTenant.DisplayName);
        }

        [TestMethod]
        public async Task ReloadConfigurationAsync_PreservesParentRelationships_AfterReloadingComplexHierarchy()
        {
            // Arrange - Create a complex hierarchy
            TenantConfiguration complexConfig = new TenantConfiguration
            {
                RequireAuthentication = true,
                Tenants = new Dictionary<string, TenantInfo>
                {
                    ["level1"] = new TenantInfo
                    {
                        ApiKey = "level1-key",
                        DisplayName = "Level 1",
                        StorageLimitBytes = 1024 * 1024 * 100,
                        IsAdmin = false,
                        SubTenants = new Dictionary<string, TenantInfo>
                        {
                            ["level2a"] = new TenantInfo
                            {
                                ApiKey = "level2a-key",
                                DisplayName = "Level 2A",
                                StorageLimitBytes = 1024 * 1024 * 50,
                                IsAdmin = false,
                                SubTenants = new Dictionary<string, TenantInfo>
                                {
                                    ["level3a"] = new TenantInfo
                                    {
                                        ApiKey = "level3a-key",
                                        DisplayName = "Level 3A",
                                        StorageLimitBytes = 1024 * 1024 * 25,
                                        IsAdmin = false,
                                        SubTenants = new Dictionary<string, TenantInfo>()
                                    }
                                }
                            },
                            ["level2b"] = new TenantInfo
                            {
                                ApiKey = "level2b-key",
                                DisplayName = "Level 2B",
                                StorageLimitBytes = 1024 * 1024 * 50,
                                IsAdmin = false,
                                SubTenants = new Dictionary<string, TenantInfo>()
                            }
                        }
                    }
                }
            };

            string jsonContent = JsonSerializer.Serialize(complexConfig, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_testConfigPath, jsonContent);

            // Act
            bool result = await _service.ReloadConfigurationAsync();

            // Assert
            Assert.IsTrue(result);

            TenantConfiguration config = _service.GetConfiguration();

            TenantInfo level1 = config.Tenants["level1"];
            TenantInfo level2a = level1.SubTenants["level2a"];
            TenantInfo level2b = level1.SubTenants["level2b"];
            TenantInfo level3a = level2a.SubTenants["level3a"];

            // Verify all parent relationships are correctly set
            Assert.IsNull(level1.Parent);
            Assert.AreSame(level1, level2a.Parent);
            Assert.AreSame(level1, level2b.Parent);
            Assert.AreSame(level2a, level3a.Parent);
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

        [TestMethod]
        public async Task CreateSubTenantAsync_CreatesNewSubTenant_WhenValidParameters()
        {
            // Arrange
            string parentTenantId = "tenant1";
            string displayName = "Test Subtenant";

            // Act
            string subTenantId = await _service.CreateSubTenantAsync(parentTenantId, displayName);

            // Assert
            Assert.IsNotNull(subTenantId);
            Assert.IsTrue(Guid.TryParse(subTenantId, out _));

            TenantConfiguration config = _service.GetConfiguration();
            Assert.IsTrue(config.Tenants[parentTenantId].SubTenants.ContainsKey(subTenantId));

            TenantInfo subTenant = config.Tenants[parentTenantId].SubTenants[subTenantId];
            Assert.AreEqual(displayName, subTenant.DisplayName);
            Assert.IsNotNull(subTenant.ApiKey);
            Assert.AreEqual(config.Tenants[parentTenantId].StorageLimitBytes, subTenant.StorageLimitBytes);
            Assert.IsFalse(subTenant.IsAdmin);
            Assert.IsNotNull(subTenant.Parent);

            TenantInfo? parent = _service.GetTenant(parentTenantId);

            Assert.IsNotNull(parent);
            Assert.AreEqual(subTenant.Parent, parent);
        }

        [TestMethod]
        public async Task CreateSubTenantAsync_ThrowsInvalidOperationException_WhenParentTenantDoesNotExist()
        {
            // Arrange
            string parentTenantId = "nonexistent";
            string displayName = "Test Subtenant";

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                _service.CreateSubTenantAsync(parentTenantId, displayName));
        }

        [TestMethod]
        public async Task CreateSubTenantAsync_ThrowsArgumentException_WhenParentTenantIdIsNull()
        {
            // Arrange
            string displayName = "Test Subtenant";

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _service.CreateSubTenantAsync(null!, displayName));
        }

        [TestMethod]
        public async Task CreateSubTenantAsync_ThrowsArgumentException_WhenDisplayNameIsNull()
        {
            // Arrange
            string parentTenantId = "tenant1";

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _service.CreateSubTenantAsync(parentTenantId, null!));
        }

        [TestMethod]
        public async Task CreateSubTenantAsync_ThrowsArgumentException_WhenDisplayNameIsEmpty()
        {
            // Arrange
            string parentTenantId = "tenant1";
            string displayName = "";

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _service.CreateSubTenantAsync(parentTenantId, displayName));
        }

        [TestMethod]
        public async Task CreateSubTenantAsync_ThrowsArgumentException_WhenDisplayNameIsWhitespace()
        {
            // Arrange
            string parentTenantId = "tenant1";
            string displayName = "   ";

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _service.CreateSubTenantAsync(parentTenantId, displayName));
        }

        [TestMethod]
        public async Task CreateSubTenantAsync_ThrowsInvalidOperationException_WhenMaxDepthReached()
        {
            // Arrange - Create a chain of 10 subtenants
            string currentTenantId = "tenant1";
            for (int i = 0; i < 10; i++)
            {
                currentTenantId = await _service.CreateSubTenantAsync(currentTenantId, $"Level {i + 1}");
            }

            // Act & Assert - Try to create an 11th level subtenant
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                _service.CreateSubTenantAsync(currentTenantId, "Level 11"));
        }

        [TestMethod]
        public void GetSubTenants_ReturnsEmptyDictionary_WhenTenantHasNoSubTenants()
        {
            // Arrange
            string tenantId = "tenant1";

            // Act
            Dictionary<string, TenantInfo> subTenants = _service.GetSubTenants(tenantId);

            // Assert
            Assert.IsNotNull(subTenants);
            Assert.AreEqual(0, subTenants.Count);
        }

        [TestMethod]
        public void GetSubTenants_ReturnsSubTenants_WhenTenantHasSubTenants()
        {
            // Arrange
            string parentTenantId = "tenant1";
            string subTenantId = "subtenant1";

            TenantInfo subTenant = new TenantInfo
            {
                ApiKey = "sub-key",
                DisplayName = "Sub Tenant",
                StorageLimitBytes = 1024 * 1024 * 50,
                IsAdmin = false
            };

            TenantConfiguration config = _service.GetConfiguration();
            subTenant.Parent = config.Tenants[parentTenantId];
            config.Tenants[parentTenantId].SubTenants[subTenantId] = subTenant;

            // Act
            Dictionary<string, TenantInfo> subTenants = _service.GetSubTenants(parentTenantId);

            // Assert
            Assert.IsNotNull(subTenants);
            Assert.AreEqual(1, subTenants.Count);
            Assert.IsTrue(subTenants.ContainsKey(subTenantId));
            Assert.AreEqual(subTenant.DisplayName, subTenants[subTenantId].DisplayName);
        }

        [TestMethod]
        public void GetSubTenants_ReturnsEmptyDictionary_WhenTenantDoesNotExist()
        {
            // Arrange
            string tenantId = "nonexistent";

            // Act
            Dictionary<string, TenantInfo> subTenants = _service.GetSubTenants(tenantId);

            // Assert
            Assert.IsNotNull(subTenants);
            Assert.AreEqual(0, subTenants.Count);
        }

        [TestMethod]
        public void GetSubTenant_ReturnsSubTenant_WhenSubTenantExists()
        {
            // Arrange
            string parentTenantId = "tenant1";
            string subTenantId = "subtenant1";

            TenantInfo subTenant = new TenantInfo
            {
                ApiKey = "sub-key",
                DisplayName = "Sub Tenant",
                StorageLimitBytes = 1024 * 1024 * 50,
                IsAdmin = false
            };

            TenantConfiguration config = _service.GetConfiguration();
            subTenant.Parent = config.Tenants[parentTenantId];
            config.Tenants[parentTenantId].SubTenants[subTenantId] = subTenant;

            // Act
            TenantInfo? result = _service.GetSubTenant(parentTenantId, subTenantId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(subTenant.DisplayName, result.DisplayName);
            Assert.AreEqual(subTenant.ApiKey, result!.ApiKey);
            Assert.AreEqual(subTenant.StorageLimitBytes, result.StorageLimitBytes);
            Assert.AreEqual(subTenant.IsAdmin, result.IsAdmin);
            Assert.AreEqual(subTenant.Parent, result.Parent);
        }

        [TestMethod]
        public void GetSubTenant_ReturnsNull_WhenSubTenantDoesNotExist()
        {
            // Arrange
            string parentTenantId = "tenant1";
            string subTenantId = "nonexistent";

            // Act
            TenantInfo? result = _service.GetSubTenant(parentTenantId, subTenantId);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetSubTenant_ReturnsNull_WhenParentTenantDoesNotExist()
        {
            // Arrange
            string parentTenantId = "nonexistent";
            string subTenantId = "subtenant1";

            // Act
            TenantInfo? result = _service.GetSubTenant(parentTenantId, subTenantId);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetSubTenant_ReturnsNull_WhenParentTenantIdIsNull()
        {
            // Arrange
            string subTenantId = "subtenant1";

            // Act
            TenantInfo? result = _service.GetSubTenant(null!, subTenantId);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetSubTenant_ReturnsNull_WhenSubTenantIdIsNull()
        {
            // Arrange
            string parentTenantId = "tenant1";

            // Act
            TenantInfo? result = _service.GetSubTenant(parentTenantId, null!);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task UpdateSubTenantStorageLimitAsync_UpdatesStorageLimit_WhenSubTenantExists()
        {
            // Arrange
            string parentTenantId = "tenant1";
            string subTenantId = "subtenant1";
            long newStorageLimit = 1024 * 1024 * 200; // 200MB

            TenantInfo subTenant = new TenantInfo
            {
                ApiKey = "sub-key",
                DisplayName = "Sub Tenant",
                StorageLimitBytes = 1024 * 1024 * 50,
                IsAdmin = false
            };

            TenantConfiguration config = _service.GetConfiguration();
            subTenant.Parent = config.Tenants[parentTenantId];
            config.Tenants[parentTenantId].SubTenants[subTenantId] = subTenant;

            // Act
            bool result = await _service.UpdateSubTenantStorageLimitAsync(parentTenantId, subTenantId, newStorageLimit);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(newStorageLimit, config.Tenants[parentTenantId].SubTenants[subTenantId].StorageLimitBytes);
        }

        [TestMethod]
        public async Task UpdateSubTenantStorageLimitAsync_ReturnsFalse_WhenSubTenantDoesNotExist()
        {
            // Arrange
            string parentTenantId = "tenant1";
            string subTenantId = "nonexistent";
            long newStorageLimit = 1024 * 1024 * 200;

            // Act
            bool result = await _service.UpdateSubTenantStorageLimitAsync(parentTenantId, subTenantId, newStorageLimit);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task UpdateSubTenantStorageLimitAsync_ReturnsFalse_WhenParentTenantDoesNotExist()
        {
            // Arrange
            string parentTenantId = "nonexistent";
            string subTenantId = "subtenant1";
            long newStorageLimit = 1024 * 1024 * 200;

            // Act
            bool result = await _service.UpdateSubTenantStorageLimitAsync(parentTenantId, subTenantId, newStorageLimit);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task UpdateSubTenantStorageLimitAsync_ThrowsArgumentException_WhenParentTenantIdIsNull()
        {
            // Arrange
            string subTenantId = "subtenant1";
            long newStorageLimit = 1024 * 1024 * 200;

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _service.UpdateSubTenantStorageLimitAsync(null!, subTenantId, newStorageLimit));
        }

        [TestMethod]
        public async Task UpdateSubTenantStorageLimitAsync_ThrowsArgumentException_WhenSubTenantIdIsNull()
        {
            // Arrange
            string parentTenantId = "tenant1";
            long newStorageLimit = 1024 * 1024 * 200;

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _service.UpdateSubTenantStorageLimitAsync(parentTenantId, null!, newStorageLimit));
        }

        [TestMethod]
        public async Task UpdateSubTenantStorageLimitAsync_ThrowsArgumentException_WhenStorageLimitIsNegative()
        {
            // Arrange
            string parentTenantId = "tenant1";
            string subTenantId = "subtenant1";
            long newStorageLimit = -1024;

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _service.UpdateSubTenantStorageLimitAsync(parentTenantId, subTenantId, newStorageLimit));
        }

        [TestMethod]
        public async Task DeleteSubTenantAsync_DeletesSubTenant_WhenSubTenantExists()
        {
            // Arrange
            string parentTenantId = "tenant1";
            string subTenantId = "subtenant1";

            TenantInfo subTenant = new TenantInfo
            {
                ApiKey = "sub-key",
                DisplayName = "Sub Tenant",
                StorageLimitBytes = 1024 * 1024 * 50,
                IsAdmin = false
            };

            TenantConfiguration config = _service.GetConfiguration();
            subTenant.Parent = config.Tenants[parentTenantId];
            config.Tenants[parentTenantId].SubTenants[subTenantId] = subTenant;

            // Act
            bool result = await _service.DeleteSubTenantAsync(parentTenantId, subTenantId);

            // Assert
            Assert.IsTrue(result);
            Assert.IsFalse(config.Tenants[parentTenantId].SubTenants.ContainsKey(subTenantId));
        }

        [TestMethod]
        public async Task DeleteSubTenantAsync_ReturnsFalse_WhenSubTenantDoesNotExist()
        {
            // Arrange
            string parentTenantId = "tenant1";
            string subTenantId = "nonexistent";

            // Act
            bool result = await _service.DeleteSubTenantAsync(parentTenantId, subTenantId);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task DeleteSubTenantAsync_ReturnsFalse_WhenParentTenantDoesNotExist()
        {
            // Arrange
            string parentTenantId = "nonexistent";
            string subTenantId = "subtenant1";

            // Act
            bool result = await _service.DeleteSubTenantAsync(parentTenantId, subTenantId);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task DeleteSubTenantAsync_ThrowsArgumentException_WhenParentTenantIdIsNull()
        {
            // Arrange
            string subTenantId = "subtenant1";

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _service.DeleteSubTenantAsync(null!, subTenantId));
        }

        [TestMethod]
        public async Task DeleteSubTenantAsync_ThrowsArgumentException_WhenSubTenantIdIsNull()
        {
            // Arrange
            string parentTenantId = "tenant1";

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _service.DeleteSubTenantAsync(parentTenantId, null!));
        }

        [TestMethod]
        public async Task DeleteSubTenantAsync_DeletesDeepSubTenant_WhenUsingHigherLevelParent()
        {
            // Arrange - Create a hierarchy: tenant1 -> subtenant1 -> grandchild1
            string parentTenantId = "tenant1";
            string subTenantId = await _service.CreateSubTenantAsync(parentTenantId, "Sub Tenant");
            string grandchildId = await _service.CreateSubTenantAsync(subTenantId, "Grandchild Tenant");

            // Verify the hierarchy exists
            TenantConfiguration config = _service.GetConfiguration();
            Assert.IsTrue(config.Tenants[parentTenantId].SubTenants.ContainsKey(subTenantId));
            Assert.IsTrue(config.Tenants[parentTenantId].SubTenants[subTenantId].SubTenants.ContainsKey(grandchildId));

            // Act - Try to delete the grandchild using the root parent (tenant1)
            bool result = await _service.DeleteSubTenantAsync(parentTenantId, grandchildId);

            // Assert
            Assert.IsTrue(result);
            
            // Verify the grandchild was removed from its immediate parent (subTenantId)
            Assert.IsFalse(config.Tenants[parentTenantId].SubTenants[subTenantId].SubTenants.ContainsKey(grandchildId));
            
            // Verify the subtenant still exists
            Assert.IsTrue(config.Tenants[parentTenantId].SubTenants.ContainsKey(subTenantId));
        }

        [TestMethod]
        public async Task DeleteSubTenantAsync_ReturnsFalse_WhenSubTenantIsNotDescendantOfSpecifiedParent()
        {
            // Arrange - Create two separate hierarchies
            string parent1Id = "tenant1";
            string parent2Id = "admin";
            
            string subTenant1Id = await _service.CreateSubTenantAsync(parent1Id, "Sub Tenant 1");
            string subTenant2Id = await _service.CreateSubTenantAsync(parent2Id, "Sub Tenant 2");

            // Verify both hierarchies exist
            TenantConfiguration config = _service.GetConfiguration();
            Assert.IsTrue(config.Tenants[parent1Id].SubTenants.ContainsKey(subTenant1Id));
            Assert.IsTrue(config.Tenants[parent2Id].SubTenants.ContainsKey(subTenant2Id));

            // Act - Try to delete subTenant2 using parent1 (which should fail)
            bool result = await _service.DeleteSubTenantAsync(parent1Id, subTenant2Id);

            // Assert
            Assert.IsFalse(result);
            
            // Verify subTenant2 still exists under parent2
            Assert.IsTrue(config.Tenants[parent2Id].SubTenants.ContainsKey(subTenant2Id));
        }

        [TestMethod]
        public async Task CanCreateSubTenantAsync_ReturnsTrue_WhenTenantExistsAndDepthNotExceeded()
        {
            // Arrange
            string tenantId = "tenant1";

            // Act
            bool result = await _service.CanCreateSubTenantAsync(tenantId);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public async Task CanCreateSubTenantAsync_ReturnsFalse_WhenTenantDoesNotExist()
        {
            // Arrange
            string tenantId = "nonexistent";

            // Act
            bool result = await _service.CanCreateSubTenantAsync(tenantId);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task CanCreateSubTenantAsync_ReturnsFalse_WhenMaxDepthReached()
        {
            // Arrange - Create a chain of 10 subtenants
            string currentTenantId = "tenant1";
            for (int i = 0; i < 10; i++)
            {
                currentTenantId = await _service.CreateSubTenantAsync(currentTenantId, $"Level {i + 1}");
            }

            // Act
            bool result = await _service.CanCreateSubTenantAsync(currentTenantId);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task CanCreateSubTenantAsync_ThrowsArgumentException_WhenTenantIdIsNull()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                _service.CanCreateSubTenantAsync(null!));
        }

        [TestMethod]
        public async Task CanCreateSubTenantAsync_ReturnsFalse_WhenMaxHorizontalLimitReached()
        {
            // Arrange - Create 50 subtenants under tenant1
            string parentTenantId = "tenant1";
            for (int i = 0; i < 50; i++)
            {
                await _service.CreateSubTenantAsync(parentTenantId, $"Subtenant {i + 1}");
            }

            // Act
            bool result = await _service.CanCreateSubTenantAsync(parentTenantId);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task CanCreateSubTenantAsync_ReturnsTrue_WhenHorizontalLimitNotReached()
        {
            // Arrange - Create 49 subtenants under tenant1 (just under the limit)
            string parentTenantId = "tenant1";
            for (int i = 0; i < 49; i++)
            {
                await _service.CreateSubTenantAsync(parentTenantId, $"Subtenant {i + 1}");
            }

            // Act
            bool result = await _service.CanCreateSubTenantAsync(parentTenantId);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public async Task CreateSubTenantAsync_ThrowsInvalidOperationException_WhenMaxHorizontalLimitReached()
        {
            // Arrange - Create 50 subtenants under tenant1
            string parentTenantId = "tenant1";
            for (int i = 0; i < 50; i++)
            {
                await _service.CreateSubTenantAsync(parentTenantId, $"Subtenant {i + 1}");
            }

            // Act & Assert
            InvalidOperationException exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                _service.CreateSubTenantAsync(parentTenantId, "One Too Many"));

            // Assert
            Assert.IsTrue(exception.Message.Contains("maximum of 50 subtenants per tenant reached"));
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