using ByteShelf.Configuration;
using ByteShelfClient;
using ByteShelfCommon;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;
using System.Text.Json;

namespace ByteShelf.Integration.Tests
{
    [TestClass]
    public class IntegrationTests : IDisposable
    {
        private WebApplicationFactory<Program> _factory = null!;
        private HttpClient _httpClient = null!;
        private HttpShelfFileProvider _client = null!;
        private string _tempStoragePath = null!;
        private const string TestApiKey = "dev-api-key-12345";

        [TestInitialize]
        public void Setup()
        {
            _tempStoragePath = Path.Combine(Path.GetTempPath(), $"ByteShelf-Integration-{Guid.NewGuid()}");

            // Clean up any previous test files
            if (Directory.Exists(_tempStoragePath))
            {
                Directory.Delete(_tempStoragePath, true);
            }

            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");

            // Create tenant configuration file for tests
            Directory.CreateDirectory(_tempStoragePath);
            CreateTestTenantConfiguration(tenantConfigPath);

            // Set environment variables for configuration
            Environment.SetEnvironmentVariable("BYTESHELF_TENANT_CONFIG_PATH", tenantConfigPath);
            Environment.SetEnvironmentVariable("BYTESHELF_STORAGE_PATH", _tempStoragePath);

            _factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseContentRoot(Directory.GetCurrentDirectory());
                    builder.ConfigureAppConfiguration((context, config) =>
                    {
                        // Override configuration for tests
                        config.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["Authentication:ApiKey"] = TestApiKey,
                            ["Authentication:RequireAuthentication"] = "true"
                        });
                    });
                });

            _httpClient = _factory.CreateClient();
            _client = new HttpShelfFileProvider(_httpClient, TestApiKey);
        }

        private void CreateTestTenantConfiguration(string configPath)
        {
            TenantConfiguration config = new TenantConfiguration
            {
                Tenants = new Dictionary<string, TenantInfo>
                {
                    [TestApiKey] = new TenantInfo
                    {
                        ApiKey = TestApiKey,
                        DisplayName = "Test Tenant",
                        StorageLimitBytes = 1000000000L, // 1GB
                        IsAdmin = false
                    }
                }
            };

            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(configPath, json);
        }

        private void CreateHierarchicalTenantConfiguration(string configPath)
        {
            TenantConfiguration config = new TenantConfiguration
            {
                Tenants = new Dictionary<string, TenantInfo>
                {
                    ["parent-tenant"] = new TenantInfo
                    {
                        ApiKey = "parent-api-key",
                        DisplayName = "Parent Tenant",
                        StorageLimitBytes = 1000000000L, // 1GB
                        IsAdmin = false,
                        SubTenants = new Dictionary<string, TenantInfo>
                        {
                            ["subtenant-1"] = new TenantInfo
                            {
                                ApiKey = "subtenant-1-api-key",
                                DisplayName = "Subtenant 1",
                                StorageLimitBytes = 500000000L, // 500MB
                                IsAdmin = false,
                                SubTenants = new Dictionary<string, TenantInfo>()
                            },
                            ["subtenant-2"] = new TenantInfo
                            {
                                ApiKey = "subtenant-2-api-key",
                                DisplayName = "Subtenant 2",
                                StorageLimitBytes = 500000000L, // 500MB
                                IsAdmin = false,
                                SubTenants = new Dictionary<string, TenantInfo>()
                            }
                        }
                    }
                }
            };

            // Set parent references
            config.Tenants["parent-tenant"].SubTenants["subtenant-1"].Parent = config.Tenants["parent-tenant"];
            config.Tenants["parent-tenant"].SubTenants["subtenant-2"].Parent = config.Tenants["parent-tenant"];

            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(configPath, json);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _httpClient?.Dispose();
            _factory?.Dispose();

            if (Directory.Exists(_tempStoragePath))
            {
                Directory.Delete(_tempStoragePath, true);
            }
        }

        [TestMethod]
        public async Task FullPipeline_SmallFile_UploadAndDownloadSuccessfully()
        {
            // Arrange
            string originalContent = "Hello, ByteShelf! This is a test file.";
            string filename = "test.txt";
            string contentType = "text/plain";
            using MemoryStream uploadStream = new MemoryStream(Encoding.UTF8.GetBytes(originalContent));

            // Act - Upload
            Guid fileId = await _client.WriteFileAsync(filename, contentType, uploadStream);

            // Assert - Verify file was created
            Assert.AreNotEqual(Guid.Empty, fileId);

            // Act - Download
            ShelfFile downloadedFile = await _client.ReadFileAsync(fileId);

            // Assert - Verify metadata
            Assert.AreEqual(filename, downloadedFile.Metadata.OriginalFilename);
            Assert.AreEqual(contentType, downloadedFile.Metadata.ContentType);
            Assert.AreEqual(originalContent.Length, downloadedFile.Metadata.FileSize);
            Assert.AreEqual(fileId, downloadedFile.Metadata.Id);

            // Assert - Verify content
            using Stream contentStream = downloadedFile.GetContentStream();
            using StreamReader reader = new StreamReader(contentStream);
            string downloadedContent = reader.ReadToEnd();
            Assert.AreEqual(originalContent, downloadedContent);
        }

        [TestMethod]
        public async Task FullPipeline_LargeFile_UploadAndDownloadSuccessfully()
        {
            // Arrange - Create a file larger than the chunk size
            string largeContent = new string('A', 10000000) + new string('B', 10000000) + new string('C', 10000000);
            string filename = "large-file.txt";
            string contentType = "text/plain";
            using MemoryStream uploadStream = new MemoryStream(Encoding.UTF8.GetBytes(largeContent));

            // Act - Upload
            Guid fileId = await _client.WriteFileAsync(filename, contentType, uploadStream);

            // Assert - Verify file was created
            Assert.AreNotEqual(Guid.Empty, fileId);

            // Act - Download
            ShelfFile downloadedFile = await _client.ReadFileAsync(fileId);

            // Assert - Verify metadata
            Assert.AreEqual(filename, downloadedFile.Metadata.OriginalFilename);
            Assert.AreEqual(contentType, downloadedFile.Metadata.ContentType);
            Assert.AreEqual(largeContent.Length, downloadedFile.Metadata.FileSize);
            Assert.IsTrue(downloadedFile.Metadata.ChunkIds.Count > 1, "Large file should be split into multiple chunks");

            // Assert - Verify content
            using Stream contentStream = downloadedFile.GetContentStream();
            using StreamReader reader = new StreamReader(contentStream);
            string downloadedContent = reader.ReadToEnd();
            Assert.AreEqual(largeContent, downloadedContent);
        }

        [TestMethod]
        public async Task FullPipeline_MultipleFiles_ListAndDeleteSuccessfully()
        {
            // Arrange - Upload multiple files
            List<Guid> fileIds = new List<Guid>();

            for (int i = 1; i <= 3; i++)
            {
                string content = $"Content for file {i}";
                string filename = $"file{i}.txt";
                using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
                Guid fileId = await _client.WriteFileAsync(filename, "text/plain", stream);
                fileIds.Add(fileId);
            }

            // Act - List files
            IEnumerable<ShelfFileMetadata> files = await _client.GetFilesAsync();

            // Assert - Verify all files are listed
            List<ShelfFileMetadata> fileList = new List<ShelfFileMetadata>(files);
            Assert.AreEqual(3, fileList.Count);
            Assert.IsTrue(fileList.Exists(f => f.OriginalFilename == "file1.txt"));
            Assert.IsTrue(fileList.Exists(f => f.OriginalFilename == "file2.txt"));
            Assert.IsTrue(fileList.Exists(f => f.OriginalFilename == "file3.txt"));

            // Act - Delete one file
            await _client.DeleteFileAsync(fileIds[0]);

            // Assert - Verify file is deleted
            IEnumerable<ShelfFileMetadata> filesAfterDelete = await _client.GetFilesAsync();
            List<ShelfFileMetadata> fileListAfterDelete = new List<ShelfFileMetadata>(filesAfterDelete);
            Assert.AreEqual(2, fileListAfterDelete.Count);
            Assert.IsFalse(fileListAfterDelete.Exists(f => f.OriginalFilename == "file1.txt"));
        }

        [TestMethod]
        public async Task FullPipeline_BinaryFile_UploadAndDownloadSuccessfully()
        {
            // Arrange - Create binary content
            byte[] originalBytes = new byte[1024];
            Random random = new Random(42); // Fixed seed for reproducible tests
            random.NextBytes(originalBytes);

            string filename = "binary.dat";
            string contentType = "application/octet-stream";
            using MemoryStream uploadStream = new MemoryStream(originalBytes);

            // Act - Upload
            Guid fileId = await _client.WriteFileAsync(filename, contentType, uploadStream);

            // Assert - Verify file was created
            Assert.AreNotEqual(Guid.Empty, fileId);

            // Act - Download
            ShelfFile downloadedFile = await _client.ReadFileAsync(fileId);

            // Assert - Verify metadata
            Assert.AreEqual(filename, downloadedFile.Metadata.OriginalFilename);
            Assert.AreEqual(contentType, downloadedFile.Metadata.ContentType);
            Assert.AreEqual(originalBytes.Length, downloadedFile.Metadata.FileSize);

            // Assert - Verify binary content
            using Stream contentStream = downloadedFile.GetContentStream();
            using MemoryStream downloadedStream = new MemoryStream();
            await contentStream.CopyToAsync(downloadedStream);
            byte[] downloadedBytes = downloadedStream.ToArray();

            Assert.AreEqual(originalBytes.Length, downloadedBytes.Length);
            for (int i = 0; i < originalBytes.Length; i++)
            {
                Assert.AreEqual(originalBytes[i], downloadedBytes[i], $"Byte mismatch at position {i}");
            }
        }

        [TestMethod]
        public async Task FullPipeline_Chunking_VerifiesChunkCreation()
        {
            // Arrange - Create content that will definitely be chunked (larger than 1MB)
            string content = new string('X', 30000000); // ~30MB, larger than ~25MB chunk size
            string filename = "chunked-file.txt";
            using MemoryStream uploadStream = new MemoryStream(Encoding.UTF8.GetBytes(content));

            // Act - Upload
            Guid fileId = await _client.WriteFileAsync(filename, "text/plain", uploadStream);

            // Act - Download
            ShelfFile downloadedFile = await _client.ReadFileAsync(fileId);

            // Assert - Verify chunking occurred
            Assert.IsTrue(downloadedFile.Metadata.ChunkIds.Count > 1, "File should be split into multiple chunks");

            // Verify content integrity
            using Stream contentStream = downloadedFile.GetContentStream();
            using StreamReader reader = new StreamReader(contentStream);
            string downloadedContent = reader.ReadToEnd();
            Assert.AreEqual(content, downloadedContent);
        }

        [TestMethod]
        public async Task FullPipeline_ConcurrentUploads_HandleMultipleRequests()
        {
            // Arrange
            List<Task<Guid>> uploadTasks = new List<Task<Guid>>();
            List<MemoryStream> streams = new List<MemoryStream>();

            // Start 5 concurrent uploads with small files
            for (int i = 0; i < 5; i++)
            {
                string content = $"Concurrent file {i}";
                string filename = $"concurrent{i}.txt";
                MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
                streams.Add(stream);
                uploadTasks.Add(_client.WriteFileAsync(filename, "text/plain", stream));
            }

            try
            {
                // Act - Wait for all uploads to complete
                Guid[] fileIds = await Task.WhenAll(uploadTasks);

                // Assert - Verify all files were uploaded successfully
                Assert.AreEqual(5, fileIds.Length);
                foreach (Guid fileId in fileIds)
                {
                    Assert.AreNotEqual(Guid.Empty, fileId);

                    // Verify each file can be downloaded
                    ShelfFile file = await _client.ReadFileAsync(fileId);
                    Assert.IsNotNull(file);
                    Assert.IsNotNull(file.Metadata);
                }
            }
            finally
            {
                // Clean up streams
                foreach (MemoryStream stream in streams)
                {
                    stream.Dispose();
                }
            }
        }

        [TestMethod]
        public async Task Authentication_ValidApiKey_AllowsAccess()
        {
            // Arrange - Create a client with valid API key
            using HttpClient validClient = _factory.CreateClient();
            validClient.DefaultRequestHeaders.Add("X-API-Key", TestApiKey);
            HttpShelfFileProvider validProvider = new HttpShelfFileProvider(validClient, TestApiKey);

            // Act - Try to list files (should succeed)
            IEnumerable<ShelfFileMetadata> files = await validProvider.GetFilesAsync();

            // Assert - Should not throw an exception
            Assert.IsNotNull(files);
        }

        [TestMethod]
        public async Task Authentication_InvalidApiKey_ReturnsUnauthorized()
        {
            // Arrange - Create a client with invalid API key
            using HttpClient invalidClient = _factory.CreateClient();
            invalidClient.DefaultRequestHeaders.Add("X-API-Key", "invalid-key");
            HttpShelfFileProvider invalidProvider = new HttpShelfFileProvider(invalidClient, "invalid-key");

            // Act & Assert - Should throw HttpRequestException with 401 status
            HttpRequestException? exception = await Assert.ThrowsExceptionAsync<HttpRequestException>(
                async () => await invalidProvider.GetFilesAsync());

            Assert.IsTrue(exception.Message.Contains("401") || exception.Message.Contains("Unauthorized"));
        }

        [TestMethod]
        public void Authentication_MissingApiKey_ReturnsUnauthorized()
        {
            // Arrange - Create a client without API key
            using HttpClient noKeyClient = _factory.CreateClient();

            // Act & Assert - Constructor should throw ArgumentNullException for missing API key
            Assert.ThrowsException<ArgumentNullException>(
                () => new HttpShelfFileProvider(noKeyClient, null!));
        }

        [TestMethod]
        public async Task MultiTenancy_TenantIsolation_FilesAreIsolated()
        {
            // Arrange - Create two different tenants
            const string tenant1ApiKey = "tenant1-api-key";
            const string tenant2ApiKey = "tenant2-api-key";

            // Create tenant configuration with both tenants
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateMultiTenantConfiguration(tenantConfigPath, tenant1ApiKey, tenant2ApiKey);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            // Use the existing factory but with updated tenant configuration
            using HttpClient client1 = _factory.CreateClient();
            using HttpClient client2 = _factory.CreateClient();
            HttpShelfFileProvider provider1 = new HttpShelfFileProvider(client1, tenant1ApiKey);
            HttpShelfFileProvider provider2 = new HttpShelfFileProvider(client2, tenant2ApiKey);

            // Act - Upload files to both tenants
            string content1 = "Tenant 1 content";
            string content2 = "Tenant 2 content";

            using MemoryStream stream1 = new MemoryStream(Encoding.UTF8.GetBytes(content1));
            using MemoryStream stream2 = new MemoryStream(Encoding.UTF8.GetBytes(content2));

            Guid fileId1 = await provider1.WriteFileAsync("tenant1-file.txt", "text/plain", stream1);
            Guid fileId2 = await provider2.WriteFileAsync("tenant2-file.txt", "text/plain", stream2);

            // Assert - Each tenant should only see their own files
            IEnumerable<ShelfFileMetadata> tenant1Files = await provider1.GetFilesAsync();
            IEnumerable<ShelfFileMetadata> tenant2Files = await provider2.GetFilesAsync();

            List<ShelfFileMetadata> tenant1FileList = new List<ShelfFileMetadata>(tenant1Files);
            List<ShelfFileMetadata> tenant2FileList = new List<ShelfFileMetadata>(tenant2Files);

            Assert.AreEqual(1, tenant1FileList.Count);
            Assert.AreEqual(1, tenant2FileList.Count);
            Assert.AreEqual("tenant1-file.txt", tenant1FileList[0].OriginalFilename);
            Assert.AreEqual("tenant2-file.txt", tenant2FileList[0].OriginalFilename);

            // Assert - Tenants cannot access each other's files
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(
                async () => await provider1.ReadFileAsync(fileId2));
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(
                async () => await provider2.ReadFileAsync(fileId1));
        }

        [TestMethod]
        public async Task MultiTenancy_StorageLocation_FilesStoredInCorrectDirectories()
        {
            // Arrange - Create a tenant and upload a file
            const string tenantApiKey = "storage-test-tenant";
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateMultiTenantConfiguration(tenantConfigPath, tenantApiKey);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient client = _factory.CreateClient();
            HttpShelfFileProvider provider = new HttpShelfFileProvider(client, tenantApiKey);

            string content = "Test content for storage location verification";
            using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            Guid fileId = await provider.WriteFileAsync("storage-test.txt", "text/plain", stream);

            // Act - Get the file to verify it was stored correctly
            ShelfFile file = await provider.ReadFileAsync(fileId);

            // Assert - Verify the file was stored in the correct tenant directory
            string expectedTenantBinPath = Path.Combine(_tempStoragePath, tenantApiKey, "bin");
            string expectedTenantMetadataPath = Path.Combine(_tempStoragePath, tenantApiKey, "metadata");

            Assert.IsTrue(Directory.Exists(expectedTenantBinPath), "Tenant binary directory should exist");
            Assert.IsTrue(Directory.Exists(expectedTenantMetadataPath), "Tenant metadata directory should exist");

            // Verify chunks exist
            foreach (Guid chunkId in file.Metadata.ChunkIds)
            {
                string chunkPath = Path.Combine(expectedTenantBinPath, $"{chunkId}.bin");
                Assert.IsTrue(File.Exists(chunkPath), $"Chunk file should exist: {chunkPath}");
            }

            // Verify metadata file exists
            string metadataPath = Path.Combine(expectedTenantMetadataPath, $"{fileId}.json");
            Assert.IsTrue(File.Exists(metadataPath), "Metadata file should exist");

            // Verify content integrity
            using Stream contentStream = file.GetContentStream();
            using StreamReader reader = new StreamReader(contentStream);
            string downloadedContent = reader.ReadToEnd();
            Assert.AreEqual(content, downloadedContent);
        }

        [TestMethod]
        public async Task MultiTenancy_CrossTenantOperations_FailWithUnauthorized()
        {
            // Arrange - Create two tenants
            const string tenant1ApiKey = "tenant1-api-key";
            const string tenant2ApiKey = "tenant2-api-key";

            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateMultiTenantConfiguration(tenantConfigPath, tenant1ApiKey, tenant2ApiKey);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient client1 = _factory.CreateClient();
            using HttpClient client2 = _factory.CreateClient();
            HttpShelfFileProvider provider1 = new HttpShelfFileProvider(client1, tenant1ApiKey);
            HttpShelfFileProvider provider2 = new HttpShelfFileProvider(client2, tenant2ApiKey);

            // Upload a file to tenant 1
            string content = "Tenant 1 content";
            using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            Guid fileId = await provider1.WriteFileAsync("test.txt", "text/plain", stream);

            // Act & Assert - Tenant 2 should not be able to access tenant 1's file
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(
                async () => await provider2.ReadFileAsync(fileId));

            await Assert.ThrowsExceptionAsync<FileNotFoundException>(
                async () => await provider2.DeleteFileAsync(fileId));

            // Verify tenant 2's file list is empty
            IEnumerable<ShelfFileMetadata> tenant2Files = await provider2.GetFilesAsync();
            Assert.AreEqual(0, new List<ShelfFileMetadata>(tenant2Files).Count);
        }

        [TestMethod]
        public async Task MultiTenancy_StorageQuota_PerTenantQuotaEnforcement()
        {
            // Arrange - Create a tenant with a small quota
            const string tenantApiKey = "quota-test-tenant";
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateTenantWithQuota(tenantConfigPath, tenantApiKey, 100); // 100 bytes quota

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient client = _factory.CreateClient();
            HttpShelfFileProvider provider = new HttpShelfFileProvider(client, tenantApiKey);

            // Act - Upload a small file (should succeed)
            string smallContent = "Small file";
            using MemoryStream smallStream = new MemoryStream(Encoding.UTF8.GetBytes(smallContent));
            Guid smallFileId = await provider.WriteFileAsync("small.txt", "text/plain", smallStream);

            // Act - Try to upload a large file (should fail)
            string largeContent = new string('X', 200); // 200 bytes, exceeds 100 byte quota
            using MemoryStream largeStream = new MemoryStream(Encoding.UTF8.GetBytes(largeContent));

            // Assert - Large file upload should fail
            await Assert.ThrowsExceptionAsync<Exception>(
                async () => await provider.WriteFileAsync("large.txt", "text/plain", largeStream));

            // Verify small file still exists and is accessible
            ShelfFile smallFile = await provider.ReadFileAsync(smallFileId);
            using Stream contentStream = smallFile.GetContentStream();
            using StreamReader reader = new StreamReader(contentStream);
            string downloadedContent = reader.ReadToEnd();
            Assert.AreEqual(smallContent, downloadedContent);
        }

        private void CreateMultiTenantConfiguration(string configPath, params string[] apiKeys)
        {
            TenantConfiguration config = new TenantConfiguration
            {
                Tenants = new Dictionary<string, TenantInfo>()
            };

            foreach (string apiKey in apiKeys)
            {
                config.Tenants[apiKey] = new TenantInfo
                {
                    ApiKey = apiKey,
                    DisplayName = $"Test Tenant {apiKey}",
                    StorageLimitBytes = 1000000000L, // 1GB
                    IsAdmin = false
                };
            }

            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(configPath, json);
        }

        private void CreateTenantWithQuota(string configPath, string apiKey, long quotaBytes)
        {
            TenantConfiguration config = new TenantConfiguration
            {
                Tenants = new Dictionary<string, TenantInfo>
                {
                    [apiKey] = new TenantInfo
                    {
                        ApiKey = apiKey,
                        DisplayName = $"Quota Test Tenant",
                        StorageLimitBytes = quotaBytes,
                        IsAdmin = false
                    }
                }
            };

            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(configPath, json);
        }



        [TestMethod]
        public async Task TenantSpecificAccess_ParentCanListSubtenantFiles()
        {
            // Arrange - Create hierarchical tenant configuration
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfiguration(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient parentClient = _factory.CreateClient();
            using HttpClient subtenantClient = _factory.CreateClient();
            HttpShelfFileProvider parentProvider = new HttpShelfFileProvider(parentClient, "parent-api-key");
            HttpShelfFileProvider subtenantProvider = new HttpShelfFileProvider(subtenantClient, "subtenant-1-api-key");

            // Upload files to subtenant
            string content1 = "Subtenant file 1";
            string content2 = "Subtenant file 2";

            using MemoryStream stream1 = new MemoryStream(Encoding.UTF8.GetBytes(content1));
            using MemoryStream stream2 = new MemoryStream(Encoding.UTF8.GetBytes(content2));

            Guid fileId1 = await subtenantProvider.WriteFileAsync("subtenant-file-1.txt", "text/plain", stream1);
            Guid fileId2 = await subtenantProvider.WriteFileAsync("subtenant-file-2.txt", "text/plain", stream2);

            // Act - Parent lists files from subtenant
            IEnumerable<ShelfFileMetadata> subtenantFiles = await parentProvider.GetFilesForTenantAsync("subtenant-1");

            // Assert - Parent should see subtenant's files
            List<ShelfFileMetadata> fileList = new List<ShelfFileMetadata>(subtenantFiles);
            Assert.AreEqual(2, fileList.Count);
            Assert.IsTrue(fileList.Exists(f => f.OriginalFilename == "subtenant-file-1.txt"));
            Assert.IsTrue(fileList.Exists(f => f.OriginalFilename == "subtenant-file-2.txt"));
        }

        [TestMethod]
        public async Task TenantSpecificAccess_ParentCanReadSubtenantFiles()
        {
            // Arrange - Create hierarchical tenant configuration
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfiguration(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient parentClient = _factory.CreateClient();
            using HttpClient subtenantClient = _factory.CreateClient();
            HttpShelfFileProvider parentProvider = new HttpShelfFileProvider(parentClient, "parent-api-key");
            HttpShelfFileProvider subtenantProvider = new HttpShelfFileProvider(subtenantClient, "subtenant-1-api-key");

            // Upload file to subtenant
            string content = "Subtenant file content";
            using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            Guid fileId = await subtenantProvider.WriteFileAsync("subtenant-file.txt", "text/plain", stream);

            // Act - Parent reads file from subtenant
            ShelfFile downloadedFile = await parentProvider.ReadFileForTenantAsync("subtenant-1", fileId);

            // Assert - Parent should be able to read subtenant's file
            Assert.AreEqual("subtenant-file.txt", downloadedFile.Metadata.OriginalFilename);
            Assert.AreEqual("text/plain", downloadedFile.Metadata.ContentType);
            Assert.AreEqual(content.Length, downloadedFile.Metadata.FileSize);

            // Verify content
            using Stream contentStream = downloadedFile.GetContentStream();
            using StreamReader reader = new StreamReader(contentStream);
            string downloadedContent = reader.ReadToEnd();
            Assert.AreEqual(content, downloadedContent);
        }

        [TestMethod]
        public async Task TenantSpecificAccess_ParentCanWriteToSubtenant()
        {
            // Arrange - Create hierarchical tenant configuration
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfiguration(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient parentClient = _factory.CreateClient();
            using HttpClient subtenantClient = _factory.CreateClient();
            HttpShelfFileProvider parentProvider = new HttpShelfFileProvider(parentClient, "parent-api-key");
            HttpShelfFileProvider subtenantProvider = new HttpShelfFileProvider(subtenantClient, "subtenant-1-api-key");

            // Act - Parent uploads file to subtenant
            string content = "File uploaded by parent to subtenant";
            using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            Guid fileId = await parentProvider.WriteFileForTenantAsync("subtenant-1", "parent-uploaded-file.txt", "text/plain", stream);

            // Assert - File should be accessible by subtenant
            ShelfFile subtenantFile = await subtenantProvider.ReadFileAsync(fileId);
            Assert.AreEqual("parent-uploaded-file.txt", subtenantFile.Metadata.OriginalFilename);
            Assert.AreEqual("text/plain", subtenantFile.Metadata.ContentType);

            // Verify content
            using Stream contentStream = subtenantFile.GetContentStream();
            using StreamReader reader = new StreamReader(contentStream);
            string downloadedContent = reader.ReadToEnd();
            Assert.AreEqual(content, downloadedContent);
        }

        [TestMethod]
        public async Task TenantSpecificAccess_ParentCanDeleteSubtenantFiles()
        {
            // Arrange - Create hierarchical tenant configuration
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfiguration(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient parentClient = _factory.CreateClient();
            using HttpClient subtenantClient = _factory.CreateClient();
            HttpShelfFileProvider parentProvider = new HttpShelfFileProvider(parentClient, "parent-api-key");
            HttpShelfFileProvider subtenantProvider = new HttpShelfFileProvider(subtenantClient, "subtenant-1-api-key");

            // Upload file to subtenant
            string content = "File to be deleted by parent";
            using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            Guid fileId = await subtenantProvider.WriteFileAsync("file-to-delete.txt", "text/plain", stream);

            // Verify file exists
            IEnumerable<ShelfFileMetadata> filesBeforeDelete = await subtenantProvider.GetFilesAsync();
            Assert.AreEqual(1, new List<ShelfFileMetadata>(filesBeforeDelete).Count);

            // Act - Parent deletes file from subtenant
            await parentProvider.DeleteFileForTenantAsync("subtenant-1", fileId);

            // Assert - File should be deleted
            IEnumerable<ShelfFileMetadata> filesAfterDelete = await subtenantProvider.GetFilesAsync();
            Assert.AreEqual(0, new List<ShelfFileMetadata>(filesAfterDelete).Count);

            // Verify file cannot be read
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(
                async () => await subtenantProvider.ReadFileAsync(fileId));
        }

        [TestMethod]
        public async Task TenantSpecificAccess_SubtenantCannotAccessParentFiles()
        {
            // Arrange - Create hierarchical tenant configuration
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfiguration(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient parentClient = _factory.CreateClient();
            using HttpClient subtenantClient = _factory.CreateClient();
            HttpShelfFileProvider parentProvider = new HttpShelfFileProvider(parentClient, "parent-api-key");
            HttpShelfFileProvider subtenantProvider = new HttpShelfFileProvider(subtenantClient, "subtenant-1-api-key");

            // Upload file to parent
            string content = "Parent file";
            using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            Guid fileId = await parentProvider.WriteFileAsync("parent-file.txt", "text/plain", stream);

            // Act & Assert - Subtenant should not be able to access parent's files
            await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(
                async () => await subtenantProvider.GetFilesForTenantAsync("parent-tenant"));

            await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(
                async () => await subtenantProvider.ReadFileForTenantAsync("parent-tenant", fileId));

            await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(
                async () => await subtenantProvider.WriteFileForTenantAsync("parent-tenant", "test.txt", "text/plain", stream));

            await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(
                async () => await subtenantProvider.DeleteFileForTenantAsync("parent-tenant", fileId));
        }

        [TestMethod]
        public async Task TenantSpecificAccess_SiblingSubtenantsCannotAccessEachOther()
        {
            // Arrange - Create hierarchical tenant configuration
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfiguration(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient subtenant1Client = _factory.CreateClient();
            using HttpClient subtenant2Client = _factory.CreateClient();
            HttpShelfFileProvider subtenant1Provider = new HttpShelfFileProvider(subtenant1Client, "subtenant-1-api-key");
            HttpShelfFileProvider subtenant2Provider = new HttpShelfFileProvider(subtenant2Client, "subtenant-2-api-key");

            // Upload file to subtenant 1
            string content = "Subtenant 1 file";
            using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            Guid fileId = await subtenant1Provider.WriteFileAsync("subtenant1-file.txt", "text/plain", stream);

            // Act & Assert - Subtenant 2 should not be able to access subtenant 1's files
            await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(
                async () => await subtenant2Provider.GetFilesForTenantAsync("subtenant-1"));

            await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(
                async () => await subtenant2Provider.ReadFileForTenantAsync("subtenant-1", fileId));

            await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(
                async () => await subtenant2Provider.WriteFileForTenantAsync("subtenant-1", "test.txt", "text/plain", stream));

            await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(
                async () => await subtenant2Provider.DeleteFileForTenantAsync("subtenant-1", fileId));
        }

        [TestMethod]
        public async Task TenantSpecificAccess_NonExistentTenant_ReturnsNotFound()
        {
            // Arrange - Create hierarchical tenant configuration
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfiguration(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient parentClient = _factory.CreateClient();
            HttpShelfFileProvider parentProvider = new HttpShelfFileProvider(parentClient, "parent-api-key");

            // Act & Assert - Accessing non-existent tenant should return 404
            await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(
                async () => await parentProvider.GetFilesForTenantAsync("non-existent-tenant"));

            await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(
                async () => await parentProvider.ReadFileForTenantAsync("non-existent-tenant", Guid.NewGuid()));

            await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(
                async () => await parentProvider.WriteFileForTenantAsync("non-existent-tenant", "test.txt", "text/plain", new MemoryStream()));

            await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(
                async () => await parentProvider.DeleteFileForTenantAsync("non-existent-tenant", Guid.NewGuid()));
        }

        [TestMethod]
        public async Task TenantSpecificAccess_ParentCanAccessMultipleSubtenants()
        {
            // Arrange - Create hierarchical tenant configuration
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfiguration(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient parentClient = _factory.CreateClient();
            using HttpClient subtenant1Client = _factory.CreateClient();
            using HttpClient subtenant2Client = _factory.CreateClient();
            HttpShelfFileProvider parentProvider = new HttpShelfFileProvider(parentClient, "parent-api-key");
            HttpShelfFileProvider subtenant1Provider = new HttpShelfFileProvider(subtenant1Client, "subtenant-1-api-key");
            HttpShelfFileProvider subtenant2Provider = new HttpShelfFileProvider(subtenant2Client, "subtenant-2-api-key");

            // Upload files to both subtenants
            string content1 = "Subtenant 1 content";
            string content2 = "Subtenant 2 content";

            using MemoryStream stream1 = new MemoryStream(Encoding.UTF8.GetBytes(content1));
            using MemoryStream stream2 = new MemoryStream(Encoding.UTF8.GetBytes(content2));

            Guid fileId1 = await subtenant1Provider.WriteFileAsync("subtenant1-file.txt", "text/plain", stream1);
            Guid fileId2 = await subtenant2Provider.WriteFileAsync("subtenant2-file.txt", "text/plain", stream2);

            // Act - Parent accesses files from both subtenants
            IEnumerable<ShelfFileMetadata> subtenant1Files = await parentProvider.GetFilesForTenantAsync("subtenant-1");
            IEnumerable<ShelfFileMetadata> subtenant2Files = await parentProvider.GetFilesForTenantAsync("subtenant-2");

            // Assert - Parent should see files from both subtenants
            List<ShelfFileMetadata> subtenant1FileList = new List<ShelfFileMetadata>(subtenant1Files);
            List<ShelfFileMetadata> subtenant2FileList = new List<ShelfFileMetadata>(subtenant2Files);

            Assert.AreEqual(1, subtenant1FileList.Count);
            Assert.AreEqual(1, subtenant2FileList.Count);
            Assert.AreEqual("subtenant1-file.txt", subtenant1FileList[0].OriginalFilename);
            Assert.AreEqual("subtenant2-file.txt", subtenant2FileList[0].OriginalFilename);

            // Verify parent can read files from both subtenants
            ShelfFile file1 = await parentProvider.ReadFileForTenantAsync("subtenant-1", fileId1);
            ShelfFile file2 = await parentProvider.ReadFileForTenantAsync("subtenant-2", fileId2);

            using Stream contentStream1 = file1.GetContentStream();
            using Stream contentStream2 = file2.GetContentStream();
            using StreamReader reader1 = new StreamReader(contentStream1);
            using StreamReader reader2 = new StreamReader(contentStream2);

            string downloadedContent1 = reader1.ReadToEnd();
            string downloadedContent2 = reader2.ReadToEnd();

            Assert.AreEqual(content1, downloadedContent1);
            Assert.AreEqual(content2, downloadedContent2);
        }

        [TestMethod]
        public async Task TenantSpecificAccess_ChunkOperations_WorkWithTenantSpecificEndpoints()
        {
            // Arrange - Create hierarchical tenant configuration
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfiguration(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient parentClient = _factory.CreateClient();
            using HttpClient subtenantClient = _factory.CreateClient();
            HttpShelfFileProvider parentProvider = new HttpShelfFileProvider(parentClient, "parent-api-key");
            HttpShelfFileProvider subtenantProvider = new HttpShelfFileProvider(subtenantClient, "subtenant-1-api-key");

            // Upload a large file to subtenant (will be chunked)
            string largeContent = new string('X', 50000000); // ~50MB, larger than 1MB chunk size
            using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(largeContent));
            Guid fileId = await subtenantProvider.WriteFileAsync("large-file.txt", "text/plain", stream);

            // Act - Parent reads the large file from subtenant
            ShelfFile downloadedFile = await parentProvider.ReadFileForTenantAsync("subtenant-1", fileId);

            // Assert - File should be properly chunked and readable
            Assert.AreEqual("large-file.txt", downloadedFile.Metadata.OriginalFilename);
            Assert.AreEqual("text/plain", downloadedFile.Metadata.ContentType);
            Assert.AreEqual(largeContent.Length, downloadedFile.Metadata.FileSize);
            Assert.IsTrue(downloadedFile.Metadata.ChunkIds.Count > 1, "Large file should be split into multiple chunks");

            // Verify content integrity
            using Stream contentStream = downloadedFile.GetContentStream();
            using StreamReader reader = new StreamReader(contentStream);
            string downloadedContent = reader.ReadToEnd();
            Assert.AreEqual(largeContent, downloadedContent);
        }

        [TestMethod]
        public async Task TenantSpecificAccess_SharedStorageQuota_EnforcedAcrossHierarchy()
        {
            // Arrange - Create hierarchical tenant configuration with small quota
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfigurationWithSmallQuota(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient parentClient = _factory.CreateClient();
            using HttpClient subtenantClient = _factory.CreateClient();
            HttpShelfFileProvider parentProvider = new HttpShelfFileProvider(parentClient, "parent-api-key");
            HttpShelfFileProvider subtenantProvider = new HttpShelfFileProvider(subtenantClient, "subtenant-1-api-key");

            // Upload file to subtenant (should succeed)
            string content = "Subtenant file";
            using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            Guid fileId = await subtenantProvider.WriteFileAsync("subtenant-file.txt", "text/plain", stream);

            // Act - Try to upload large file to parent (should fail due to shared quota)
            string largeContent = new string('X', 1000); // 1000 bytes, exceeds 500 byte quota
            using MemoryStream largeStream = new MemoryStream(Encoding.UTF8.GetBytes(largeContent));

            // Assert - Large file upload should fail due to shared quota
            await Assert.ThrowsExceptionAsync<Exception>(
                async () => await parentProvider.WriteFileAsync("large-file.txt", "text/plain", largeStream));

            // Verify subtenant file still exists and is accessible
            ShelfFile subtenantFile = await subtenantProvider.ReadFileAsync(fileId);
            using Stream contentStream = subtenantFile.GetContentStream();
            using StreamReader reader = new StreamReader(contentStream);
            string downloadedContent = reader.ReadToEnd();
            Assert.AreEqual(content, downloadedContent);
        }

        private void CreateHierarchicalTenantConfigurationWithSmallQuota(string configPath)
        {
            TenantConfiguration config = new TenantConfiguration
            {
                Tenants = new Dictionary<string, TenantInfo>
                {
                    ["parent-tenant"] = new TenantInfo
                    {
                        ApiKey = "parent-api-key",
                        DisplayName = "Parent Tenant",
                        StorageLimitBytes = 500L, // 500 bytes total quota
                        IsAdmin = false,
                        SubTenants = new Dictionary<string, TenantInfo>
                        {
                            ["subtenant-1"] = new TenantInfo
                            {
                                ApiKey = "subtenant-1-api-key",
                                DisplayName = "Subtenant 1",
                                StorageLimitBytes = 500L, // 500 bytes (inherits from parent)
                                IsAdmin = false,
                                SubTenants = new Dictionary<string, TenantInfo>()
                            }
                        }
                    }
                }
            };

            // Set parent references
            config.Tenants["parent-tenant"].SubTenants["subtenant-1"].Parent = config.Tenants["parent-tenant"];

            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(configPath, json);
        }

        [TestMethod]
        public async Task HierarchicalSubtenantCreation_CreateSubtenantUnderSubtenant_SuccessfullyCreatesNestedStructure()
        {
            // Arrange - Create hierarchical tenant configuration
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfiguration(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient parentClient = _factory.CreateClient();
            HttpShelfFileProvider parentProvider = new HttpShelfFileProvider(parentClient, "parent-api-key");

            // Act - Create a subtenant under the parent
            string firstLevelSubtenantId = await parentProvider.CreateSubTenantAsync("First Level Department");
            Assert.IsNotNull(firstLevelSubtenantId);
            Assert.AreNotEqual(string.Empty, firstLevelSubtenantId);

            // Act - Create a subtenant under the first-level subtenant
            string secondLevelSubtenantId = await parentProvider.CreateSubTenantUnderSubTenantAsync(firstLevelSubtenantId, "Second Level Team");
            Assert.IsNotNull(secondLevelSubtenantId);
            Assert.AreNotEqual(string.Empty, secondLevelSubtenantId);

            // Assert - Verify the hierarchical structure
            Dictionary<string, TenantInfoResponse> parentSubtenants = await parentProvider.GetSubTenantsAsync();
            Assert.IsTrue(parentSubtenants.ContainsKey(firstLevelSubtenantId), "First level subtenant should exist under parent");

            // Get the first-level subtenant and verify it has the second-level subtenant
            TenantInfoResponse firstLevelSubtenant = await parentProvider.GetSubTenantAsync(firstLevelSubtenantId);
            Assert.AreEqual("First Level Department", firstLevelSubtenant.DisplayName);

            // Verify that the parent can access subtenants under the first-level subtenant
            Dictionary<string, TenantInfoResponse> secondLevelSubtenants = await parentProvider.GetSubTenantsUnderSubTenantAsync(firstLevelSubtenantId);
            Assert.IsTrue(secondLevelSubtenants.ContainsKey(secondLevelSubtenantId), "Second level subtenant should exist under first level");

            // Get the second-level subtenant and verify its properties
            TenantInfoResponse secondLevelSubtenant = await parentProvider.GetSubTenantAsync(secondLevelSubtenantId);
            Assert.AreEqual("Second Level Team", secondLevelSubtenant.DisplayName);

            // Verify that the parent can access the second-level subtenant's subtenants (if any)
            Dictionary<string, TenantInfoResponse> thirdLevelSubtenants = await parentProvider.GetSubTenantsUnderSubTenantAsync(secondLevelSubtenantId);
            Assert.AreEqual(0, thirdLevelSubtenants.Count, "Second level subtenant should have no children");
        }

        [TestMethod]
        public async Task HierarchicalSubtenantCreation_CreateSubtenantUnderNonExistentParent_ThrowsFileNotFoundException()
        {
            // Arrange - Create hierarchical tenant configuration
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfiguration(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient parentClient = _factory.CreateClient();
            HttpShelfFileProvider parentProvider = new HttpShelfFileProvider(parentClient, "parent-api-key");

            // Act & Assert - Try to create a subtenant under a non-existent parent
            string nonExistentParentId = "non-existent-parent-id";

            FileNotFoundException exception = await Assert.ThrowsExceptionAsync<FileNotFoundException>(
                async () => await parentProvider.CreateSubTenantUnderSubTenantAsync(nonExistentParentId, "Test Subtenant"));

            Assert.IsTrue(exception.Message.Contains(nonExistentParentId), "Error message should contain the non-existent parent ID");
        }

        [TestMethod]
        public async Task HierarchicalSubtenantCreation_CreateSubtenantUnderSubtenantWithFiles_CanAccessFilesInHierarchy()
        {
            // Arrange - Create hierarchical tenant configuration
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfiguration(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient parentClient = _factory.CreateClient();
            HttpShelfFileProvider parentProvider = new HttpShelfFileProvider(parentClient, "parent-api-key");

            // Create hierarchical structure
            string firstLevelSubtenantId = await parentProvider.CreateSubTenantAsync("First Level Department");
            string secondLevelSubtenantId = await parentProvider.CreateSubTenantUnderSubTenantAsync(firstLevelSubtenantId, "Second Level Team");

            // Upload files to each level using parent's access to subtenants
            string parentContent = "Parent file content";
            string firstLevelContent = "First level file content";
            string secondLevelContent = "Second level file content";

            using MemoryStream parentStream = new MemoryStream(Encoding.UTF8.GetBytes(parentContent));
            using MemoryStream firstLevelStream = new MemoryStream(Encoding.UTF8.GetBytes(firstLevelContent));
            using MemoryStream secondLevelStream = new MemoryStream(Encoding.UTF8.GetBytes(secondLevelContent));

            Guid parentFileId = await parentProvider.WriteFileAsync("parent-file.txt", "text/plain", parentStream);
            Guid firstLevelFileId = await parentProvider.WriteFileForTenantAsync(firstLevelSubtenantId, "first-level-file.txt", "text/plain", firstLevelStream);
            Guid secondLevelFileId = await parentProvider.WriteFileForTenantAsync(secondLevelSubtenantId, "second-level-file.txt", "text/plain", secondLevelStream);

            // Act & Assert - Verify parent can access files from all levels
            ShelfFile parentFile = await parentProvider.ReadFileAsync(parentFileId);
            ShelfFile firstLevelFile = await parentProvider.ReadFileForTenantAsync(firstLevelSubtenantId, firstLevelFileId);
            ShelfFile secondLevelFile = await parentProvider.ReadFileForTenantAsync(secondLevelSubtenantId, secondLevelFileId);

            using Stream parentContentStream = parentFile.GetContentStream();
            using Stream firstLevelContentStream = firstLevelFile.GetContentStream();
            using Stream secondLevelContentStream = secondLevelFile.GetContentStream();
            using StreamReader parentReader = new StreamReader(parentContentStream);
            using StreamReader firstLevelReader = new StreamReader(firstLevelContentStream);
            using StreamReader secondLevelReader = new StreamReader(secondLevelContentStream);

            string downloadedParentContent = parentReader.ReadToEnd();
            string downloadedFirstLevelContent = firstLevelReader.ReadToEnd();
            string downloadedSecondLevelContent = secondLevelReader.ReadToEnd();

            Assert.AreEqual(parentContent, downloadedParentContent);
            Assert.AreEqual(firstLevelContent, downloadedFirstLevelContent);
            Assert.AreEqual(secondLevelContent, downloadedSecondLevelContent);
        }

        [TestMethod]
        public async Task GetSubTenantsUnderSubTenant_WithValidHierarchy_ReturnsCorrectSubTenants()
        {
            // Arrange - Create hierarchical tenant configuration
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfiguration(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient parentClient = _factory.CreateClient();
            HttpShelfFileProvider parentProvider = new HttpShelfFileProvider(parentClient, "parent-api-key");

            // Create hierarchical structure
            string firstLevelSubtenantId = await parentProvider.CreateSubTenantAsync("First Level Department");
            string secondLevelSubtenantId1 = await parentProvider.CreateSubTenantUnderSubTenantAsync(firstLevelSubtenantId, "Second Level Team 1");
            string secondLevelSubtenantId2 = await parentProvider.CreateSubTenantUnderSubTenantAsync(firstLevelSubtenantId, "Second Level Team 2");

            // Act - Get subtenants under the first-level subtenant
            Dictionary<string, TenantInfoResponse> subTenantsUnderFirstLevel = await parentProvider.GetSubTenantsUnderSubTenantAsync(firstLevelSubtenantId);

            // Assert - Should return both second-level subtenants
            Assert.AreEqual(2, subTenantsUnderFirstLevel.Count);
            Assert.IsTrue(subTenantsUnderFirstLevel.ContainsKey(secondLevelSubtenantId1), "Second level subtenant 1 should be returned");
            Assert.IsTrue(subTenantsUnderFirstLevel.ContainsKey(secondLevelSubtenantId2), "Second level subtenant 2 should be returned");
            Assert.AreEqual("Second Level Team 1", subTenantsUnderFirstLevel[secondLevelSubtenantId1].DisplayName);
            Assert.AreEqual("Second Level Team 2", subTenantsUnderFirstLevel[secondLevelSubtenantId2].DisplayName);
        }

        [TestMethod]
        public async Task GetSubTenantsUnderSubTenant_WithEmptyHierarchy_ReturnsEmptyDictionary()
        {
            // Arrange - Create hierarchical tenant configuration
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfiguration(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient parentClient = _factory.CreateClient();
            HttpShelfFileProvider parentProvider = new HttpShelfFileProvider(parentClient, "parent-api-key");

            // Create a first-level subtenant with no children
            string firstLevelSubtenantId = await parentProvider.CreateSubTenantAsync("First Level Department");

            // Act - Get subtenants under the first-level subtenant
            Dictionary<string, TenantInfoResponse> subTenantsUnderFirstLevel = await parentProvider.GetSubTenantsUnderSubTenantAsync(firstLevelSubtenantId);

            // Assert - Should return empty dictionary
            Assert.AreEqual(0, subTenantsUnderFirstLevel.Count);
        }

        [TestMethod]
        public async Task GetSubTenantsUnderSubTenant_WithNonExistentParent_ThrowsFileNotFoundException()
        {
            // Arrange - Create hierarchical tenant configuration
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfiguration(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient parentClient = _factory.CreateClient();
            HttpShelfFileProvider parentProvider = new HttpShelfFileProvider(parentClient, "parent-api-key");

            // Act & Assert - Try to get subtenants under a non-existent parent
            string nonExistentParentId = "non-existent-parent-id";

            FileNotFoundException exception = await Assert.ThrowsExceptionAsync<FileNotFoundException>(
                async () => await parentProvider.GetSubTenantsUnderSubTenantAsync(nonExistentParentId));

            Assert.IsTrue(exception.Message.Contains(nonExistentParentId), "Error message should contain the non-existent parent ID");
        }

        [TestMethod]
        public async Task GetSubTenantsUnderSubTenant_WithDeepHierarchy_ReturnsCorrectSubTenants()
        {
            // Arrange - Create hierarchical tenant configuration
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfiguration(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient parentClient = _factory.CreateClient();
            HttpShelfFileProvider parentProvider = new HttpShelfFileProvider(parentClient, "parent-api-key");

            // Create a deep hierarchical structure
            string level1SubtenantId = await parentProvider.CreateSubTenantAsync("Level 1 Department");
            string level2SubtenantId = await parentProvider.CreateSubTenantUnderSubTenantAsync(level1SubtenantId, "Level 2 Team");
            string level3SubtenantId = await parentProvider.CreateSubTenantUnderSubTenantAsync(level2SubtenantId, "Level 3 Group");
            string level4SubtenantId = await parentProvider.CreateSubTenantUnderSubTenantAsync(level3SubtenantId, "Level 4 Subgroup");

            // Act - Get subtenants under each level
            Dictionary<string, TenantInfoResponse> level1SubTenants = await parentProvider.GetSubTenantsUnderSubTenantAsync(level1SubtenantId);
            Dictionary<string, TenantInfoResponse> level2SubTenants = await parentProvider.GetSubTenantsUnderSubTenantAsync(level2SubtenantId);
            Dictionary<string, TenantInfoResponse> level3SubTenants = await parentProvider.GetSubTenantsUnderSubTenantAsync(level3SubtenantId);

            // Assert - Verify each level returns the correct subtenants
            Assert.AreEqual(1, level1SubTenants.Count);
            Assert.IsTrue(level1SubTenants.ContainsKey(level2SubtenantId));
            Assert.AreEqual("Level 2 Team", level1SubTenants[level2SubtenantId].DisplayName);

            Assert.AreEqual(1, level2SubTenants.Count);
            Assert.IsTrue(level2SubTenants.ContainsKey(level3SubtenantId));
            Assert.AreEqual("Level 3 Group", level2SubTenants[level3SubtenantId].DisplayName);

            Assert.AreEqual(1, level3SubTenants.Count);
            Assert.IsTrue(level3SubTenants.ContainsKey(level4SubtenantId));
            Assert.AreEqual("Level 4 Subgroup", level3SubTenants[level4SubtenantId].DisplayName);
        }

        [TestMethod]
        public async Task GetSubTenantsUnderSubTenant_WithMultipleSiblings_ReturnsAllSiblings()
        {
            // Arrange - Create hierarchical tenant configuration
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfiguration(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient parentClient = _factory.CreateClient();
            HttpShelfFileProvider parentProvider = new HttpShelfFileProvider(parentClient, "parent-api-key");

            // Create multiple siblings under the same parent
            string parentSubtenantId = await parentProvider.CreateSubTenantAsync("Parent Department");
            string sibling1Id = await parentProvider.CreateSubTenantUnderSubTenantAsync(parentSubtenantId, "Sibling 1");
            string sibling2Id = await parentProvider.CreateSubTenantUnderSubTenantAsync(parentSubtenantId, "Sibling 2");
            string sibling3Id = await parentProvider.CreateSubTenantUnderSubTenantAsync(parentSubtenantId, "Sibling 3");

            // Act - Get all siblings under the parent
            Dictionary<string, TenantInfoResponse> siblings = await parentProvider.GetSubTenantsUnderSubTenantAsync(parentSubtenantId);

            // Assert - Should return all three siblings
            Assert.AreEqual(3, siblings.Count);
            Assert.IsTrue(siblings.ContainsKey(sibling1Id));
            Assert.IsTrue(siblings.ContainsKey(sibling2Id));
            Assert.IsTrue(siblings.ContainsKey(sibling3Id));
            Assert.AreEqual("Sibling 1", siblings[sibling1Id].DisplayName);
            Assert.AreEqual("Sibling 2", siblings[sibling2Id].DisplayName);
            Assert.AreEqual("Sibling 3", siblings[sibling3Id].DisplayName);
        }

        [TestMethod]
        public async Task GetSubTenantsUnderSubTenant_WithFilesInSubTenants_DoesNotAffectFileOperations()
        {
            // Arrange - Create hierarchical tenant configuration
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfiguration(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient parentClient = _factory.CreateClient();
            HttpShelfFileProvider parentProvider = new HttpShelfFileProvider(parentClient, "parent-api-key");

            // Create hierarchical structure
            string firstLevelSubtenantId = await parentProvider.CreateSubTenantAsync("First Level Department");
            string secondLevelSubtenantId = await parentProvider.CreateSubTenantUnderSubTenantAsync(firstLevelSubtenantId, "Second Level Team");

            // Upload a file to the second-level subtenant using parent's access
            string content = "Test file content";
            using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            Guid fileId = await parentProvider.WriteFileForTenantAsync(secondLevelSubtenantId, "test-file.txt", "text/plain", stream);

            // Act - Get subtenants under the first-level subtenant (should not affect file operations)
            Dictionary<string, TenantInfoResponse> subTenantsUnderFirstLevel = await parentProvider.GetSubTenantsUnderSubTenantAsync(firstLevelSubtenantId);

            // Assert - File operations should still work correctly
            Assert.AreEqual(1, subTenantsUnderFirstLevel.Count);
            Assert.IsTrue(subTenantsUnderFirstLevel.ContainsKey(secondLevelSubtenantId));

            // Verify the file can still be read using parent's access
            ShelfFile file = await parentProvider.ReadFileForTenantAsync(secondLevelSubtenantId, fileId);
            using Stream contentStream = file.GetContentStream();
            using StreamReader reader = new StreamReader(contentStream);
            string downloadedContent = reader.ReadToEnd();
            Assert.AreEqual(content, downloadedContent);
        }

        [TestMethod]
        public async Task GetSubTenantsUnderSubTenant_Security_SubtenantCannotAccessOtherSubTenants()
        {
            // Arrange - Create hierarchical tenant configuration
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfiguration(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient subtenant1Client = _factory.CreateClient();
            using HttpClient subtenant2Client = _factory.CreateClient();
            HttpShelfFileProvider subtenant1Provider = new HttpShelfFileProvider(subtenant1Client, "subtenant-1-api-key");
            HttpShelfFileProvider subtenant2Provider = new HttpShelfFileProvider(subtenant2Client, "subtenant-2-api-key");

            // Create subtenants under subtenant 1
            string subtenant1ChildId = await subtenant1Provider.CreateSubTenantAsync("Subtenant 1 Child");

            // Act & Assert - Subtenant 2 should not be able to access subtenant 1's subtenants
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(
                async () => await subtenant2Provider.GetSubTenantsUnderSubTenantAsync(subtenant1ChildId));

            // Also test that subtenant 2 cannot access subtenant 1's subtenants through the parent
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(
                async () => await subtenant2Provider.GetSubTenantsUnderSubTenantAsync("subtenant-1"));
        }

        [TestMethod]
        public async Task GetSubTenantsUnderSubTenant_Security_SubtenantCannotAccessParentSubTenants()
        {
            // Arrange - Create hierarchical tenant configuration
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfiguration(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient parentClient = _factory.CreateClient();
            using HttpClient subtenantClient = _factory.CreateClient();
            HttpShelfFileProvider parentProvider = new HttpShelfFileProvider(parentClient, "parent-api-key");
            HttpShelfFileProvider subtenantProvider = new HttpShelfFileProvider(subtenantClient, "subtenant-1-api-key");

            // Create a subtenant under the parent
            string parentSubtenantId = await parentProvider.CreateSubTenantAsync("Parent Subtenant");

            // Act & Assert - Subtenant should not be able to access parent's subtenants
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(
                async () => await subtenantProvider.GetSubTenantsUnderSubTenantAsync(parentSubtenantId));
        }

        [TestMethod]
        public async Task GetSubTenantsUnderSubTenant_Security_UnauthorizedTenantCannotAccessAnySubTenants()
        {
            // Arrange - Create hierarchical tenant configuration
            string tenantConfigPath = Path.Combine(_tempStoragePath, "tenant-config.json");
            CreateHierarchicalTenantConfiguration(tenantConfigPath);

            // Wait for configuration to be reloaded
            await Task.Delay(200);

            using HttpClient parentClient = _factory.CreateClient();
            HttpShelfFileProvider parentProvider = new HttpShelfFileProvider(parentClient, "parent-api-key");

            // Create a subtenant under the parent
            string parentSubtenantId = await parentProvider.CreateSubTenantAsync("Parent Subtenant");

            // Create an unauthorized client with a different API key
            using HttpClient unauthorizedClient = _factory.CreateClient();
            HttpShelfFileProvider unauthorizedProvider = new HttpShelfFileProvider(unauthorizedClient, "unauthorized-api-key");

            // Act & Assert - Unauthorized tenant should not be able to access any subtenants
            await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(
                async () => await unauthorizedProvider.GetSubTenantsUnderSubTenantAsync(parentSubtenantId));

            // Also test with a non-existent tenant ID
            await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(
                async () => await unauthorizedProvider.GetSubTenantsUnderSubTenantAsync("non-existent-tenant"));
        }

        public void Dispose()
        {
            Cleanup();
        }
    }
}