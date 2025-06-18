using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ByteShelf.Services;
using ByteShelfClient;
using ByteShelfCommon;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ByteShelf.Integration.Tests
{
    [TestClass]
    public class IntegrationTests : IDisposable
    {
        private WebApplicationFactory<Program> _factory = null!;
        private HttpClient _httpClient = null!;
        private HttpShelfFileProvider _client = null!;
        private string _tempStoragePath = null!;
        private IFileStorageService _storageService = null!;

        [TestInitialize]
        public async Task Setup()
        {
            _tempStoragePath = Path.Combine(Path.GetTempPath(), $"ByteShelf-Integration-{Guid.NewGuid()}");
            
            _factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseContentRoot(Directory.GetCurrentDirectory());
                    builder.ConfigureServices(services =>
                    {
                        // Override the storage service to use our temp directory
                        services.AddSingleton<IFileStorageService>(provider =>
                        {
                            ILogger<FileStorageService>? logger = provider.GetService<ILogger<FileStorageService>>();
                            return new FileStorageService(_tempStoragePath, logger);
                        });
                    });
                });

            _httpClient = _factory.CreateClient();
            _client = new HttpShelfFileProvider(_httpClient);
            
            // Get the storage service from the DI container for verification
            using IServiceScope scope = _factory.Services.CreateScope();
            _storageService = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
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

        public void Dispose()
        {
            Cleanup();
        }
    }
} 