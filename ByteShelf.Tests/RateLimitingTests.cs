using ByteShelf.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;

namespace ByteShelf.Tests
{
    [TestClass]
    public class RateLimitingTests
    {
        private WebApplicationFactory<Program> _factory = null!;
        private HttpClient _httpClient = null!;

        [TestInitialize]
        public void Setup()
        {
            _factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureServices(services =>
                    {
                        // Configure test tenant configuration
                        services.AddSingleton<ITenantConfigurationService>(provider =>
                        {
                            ILogger<TenantConfigurationService> logger = provider.GetService<ILogger<TenantConfigurationService>>() ??
                                       new NullLogger<TenantConfigurationService>();
                            return new TenantConfigurationService(logger);
                        });
                    });
                });

            _httpClient = _factory.CreateClient();
        }

        [TestCleanup]
        public void Cleanup()
        {
            _httpClient?.Dispose();
            _factory?.Dispose();
        }

        [TestMethod]
        public async Task RateLimiting_ChunkOperations_HighestLimit()
        {
            // Arrange - Create tenant configuration
            ITenantConfigurationService configService = _factory.Services.GetRequiredService<ITenantConfigurationService>();
            ByteShelfCommon.TenantInfo tenant = new ByteShelfCommon.TenantInfo
            {
                ApiKey = "test-key",
                DisplayName = "Test Tenant",
                StorageLimitBytes = 1024 * 1024 * 100, // 100MB
                IsAdmin = false
            };
            await configService.AddTenantAsync("test-tenant", tenant);

            // Act - Make chunk requests (10,000 per minute limit)
            List<Task<HttpResponseMessage>> requests = new List<Task<HttpResponseMessage>>();
            for (int i = 0; i < 20; i++) // Test with 20 requests (well under 10,000 limit)
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/api/chunks/test-chunk-id");
                request.Headers.Add("X-API-Key", "test-key");
                requests.Add(_httpClient.SendAsync(request));
            }

            // Wait for all requests to complete
            HttpResponseMessage[] responses = await Task.WhenAll(requests);

            // Assert - All requests should succeed (either OK or Unauthorized, but not rate limited)
            int okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
            int unauthorizedCount = responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized);
            int rateLimitedCount = responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests);

            Assert.IsTrue(rateLimitedCount == 0, 
                $"Expected no rate limited requests, but got {rateLimitedCount}. " +
                $"OK: {okCount}, Unauthorized: {unauthorizedCount}, Rate Limited: {rateLimitedCount}");
        }

        [TestMethod]
        public async Task RateLimiting_FileOperations_ModerateLimit()
        {
            // Arrange - Create tenant configuration
            ITenantConfigurationService configService = _factory.Services.GetRequiredService<ITenantConfigurationService>();
            ByteShelfCommon.TenantInfo tenant = new ByteShelfCommon.TenantInfo
            {
                ApiKey = "test-key",
                DisplayName = "Test Tenant",
                StorageLimitBytes = 1024 * 1024 * 100, // 100MB
                IsAdmin = false
            };
            await configService.AddTenantAsync("test-tenant", tenant);

            // Act - Make file requests (1,000 per 3 minutes limit)
            List<Task<HttpResponseMessage>> requests = new List<Task<HttpResponseMessage>>();
            for (int i = 0; i < 20; i++) // Test with 20 requests (well under 1,000 limit)
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/api/files");
                request.Headers.Add("X-API-Key", "test-key");
                requests.Add(_httpClient.SendAsync(request));
            }

            // Wait for all requests to complete
            HttpResponseMessage[] responses = await Task.WhenAll(requests);

            // Assert - All requests should succeed (either OK or Unauthorized, but not rate limited)
            int okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
            int unauthorizedCount = responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized);
            int rateLimitedCount = responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests);

            Assert.IsTrue(rateLimitedCount == 0, 
                $"Expected no rate limited requests, but got {rateLimitedCount}. " +
                $"OK: {okCount}, Unauthorized: {unauthorizedCount}, Rate Limited: {rateLimitedCount}");
        }

        [TestMethod]
        public async Task RateLimiting_ConfigOperations_LowerLimit()
        {
            // Arrange - Create tenant configuration
            ITenantConfigurationService configService = _factory.Services.GetRequiredService<ITenantConfigurationService>();
            ByteShelfCommon.TenantInfo tenant = new ByteShelfCommon.TenantInfo
            {
                ApiKey = "test-key",
                DisplayName = "Test Tenant",
                StorageLimitBytes = 1024 * 1024 * 100, // 100MB
                IsAdmin = false
            };
            await configService.AddTenantAsync("test-tenant", tenant);

            // Act - Make config requests (100 per minute limit)
            List<Task<HttpResponseMessage>> requests = new List<Task<HttpResponseMessage>>();
            for (int i = 0; i < 10; i++) // Test with 10 requests (well under 100 limit)
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/api/tenant/info");
                request.Headers.Add("X-API-Key", "test-key");
                requests.Add(_httpClient.SendAsync(request));
            }

            // Wait for all requests to complete
            HttpResponseMessage[] responses = await Task.WhenAll(requests);

            // Assert - All requests should succeed (either OK or Unauthorized, but not rate limited)
            int okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
            int unauthorizedCount = responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized);
            int rateLimitedCount = responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests);

            Assert.IsTrue(rateLimitedCount == 0, 
                $"Expected no rate limited requests, but got {rateLimitedCount}. " +
                $"OK: {okCount}, Unauthorized: {unauthorizedCount}, Rate Limited: {rateLimitedCount}");
        }

        [TestMethod]
        public async Task RateLimiting_ConfigOperations_ActuallyRateLimited()
        {
            // Arrange - Create tenant configuration
            ITenantConfigurationService configService = _factory.Services.GetRequiredService<ITenantConfigurationService>();
            ByteShelfCommon.TenantInfo tenant = new ByteShelfCommon.TenantInfo
            {
                ApiKey = "test-key",
                DisplayName = "Test Tenant",
                StorageLimitBytes = 1024 * 1024 * 100, // 100MB
                IsAdmin = false
            };
            await configService.AddTenantAsync("test-tenant", tenant);

            // Act - Make config requests (100 per minute limit) - make more than the limit
            List<Task<HttpResponseMessage>> requests = new List<Task<HttpResponseMessage>>();
            for (int i = 0; i < 150; i++) // Test with 150 requests (over the 100 limit)
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/api/tenant/info");
                request.Headers.Add("X-API-Key", "test-key");
                requests.Add(_httpClient.SendAsync(request));
            }

            // Wait for all requests to complete
            HttpResponseMessage[] responses = await Task.WhenAll(requests);

            // Assert - Some requests should be rate limited
            int okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
            int unauthorizedCount = responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized);
            int rateLimitedCount = responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests);

            Assert.IsTrue(rateLimitedCount > 0, 
                $"Expected some rate limited requests, but got {rateLimitedCount}. " +
                $"OK: {okCount}, Unauthorized: {unauthorizedCount}, Rate Limited: {rateLimitedCount}");
        }
    }
}