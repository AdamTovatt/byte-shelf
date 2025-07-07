using ByteShelf.Services;
using ByteShelfClient;
using ByteShelfCommon;
using ByteShelf.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
            // Arrange - Create a file larger than the chunk size (1MB = 1,048,576 bytes)
            string largeContent = new string('A', 600000) + new string('B', 600000) + new string('C', 600000);
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
            string content = new string('X', 1200000); // 1.2MB, larger than 1MB chunk size
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



        public void Dispose()
        {
            Cleanup();
        }
    }
}