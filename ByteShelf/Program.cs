using ByteShelf.Configuration;
using ByteShelf.Middleware;
using ByteShelf.Resources;
using ByteShelf.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading.RateLimiting;

namespace ByteShelf
{
    /// <summary>
    /// Main entry point for the ByteShelf file storage API server.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// <param name="args">Command line arguments passed to the application.</param>
        public static void Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            // Add services to the container
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // Configure tenant settings using external configuration service
            builder.Services.AddSingleton<ITenantConfigurationService, TenantConfigurationService>();

            // Configure chunk settings
            builder.Services.Configure<ChunkConfiguration>(builder.Configuration.GetSection("ChunkConfiguration"));
            builder.Services.AddSingleton<ChunkConfiguration>(serviceProvider =>
            {
                ChunkConfiguration config = new ChunkConfiguration();
                builder.Configuration.GetSection("ChunkConfiguration").Bind(config);

                // Override with environment variable if set
                string? envChunkSize = Environment.GetEnvironmentVariable("BYTESHELF_CHUNK_SIZE_BYTES");
                if (!string.IsNullOrWhiteSpace(envChunkSize) && int.TryParse(envChunkSize, out int chunkSize))
                {
                    config.ChunkSizeBytes = chunkSize;
                }

                return config;
            });

            // Configure file storage
            string? envStoragePath = Environment.GetEnvironmentVariable("BYTESHELF_STORAGE_PATH");
            string storagePath = envStoragePath ?? builder.Configuration["StoragePath"] ?? "byte-shelf-storage";

            // Register storage service
            builder.Services.AddSingleton<IStorageService>(serviceProvider =>
            {
                ITenantConfigurationService configService = serviceProvider.GetRequiredService<ITenantConfigurationService>();
                ILogger<StorageService>? logger = serviceProvider.GetService<ILogger<StorageService>>();
                return new StorageService(configService, logger ?? new NullLogger<StorageService>(), storagePath);
            });

            // Register file storage service
            builder.Services.AddSingleton<IFileStorageService>(serviceProvider =>
            {
                ILogger<FileStorageService>? logger = serviceProvider.GetService<ILogger<FileStorageService>>();
                IStorageService storageService = serviceProvider.GetRequiredService<IStorageService>();
                return new FileStorageService(storagePath, storageService, logger ?? new NullLogger<FileStorageService>());
            });

            // Configure rate limiting
            ConfigureRateLimiting(builder.Services);

            WebApplication app = builder.Build();

            // Verify embedded resources on startup
            try
            {
                ResourceHelper.Instance.VerifyResourceMappings();
                app.Logger.LogInformation("Embedded resource mappings verified successfully");
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Embedded resource verification failed");
                throw;
            }

            // Configure the HTTP request pipeline
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            // Add API key authentication middleware
            app.UseApiKeyAuthentication();

            // Add rate limiting middleware
            app.UseRateLimiter();

            app.UseAuthorization();
            app.MapControllers();

            app.Run();
        }

        /// <summary>
        /// Configures rate limiting with different limits per endpoint type.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        private static void ConfigureRateLimiting(IServiceCollection services)
        {
            services.AddRateLimiter(options =>
            {
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                {
                    string apiKey = httpContext.Request.Headers["X-API-Key"].ToString() ?? "no-key";
                    string path = httpContext.Request.Path.ToString();

                    // Different limits based on endpoint type
                    if (path.StartsWith("/api/chunks"))
                    {
                        // Chunk operations: 10,000 per minute (high throughput for file uploads/downloads)
                        return RateLimitPartition.GetFixedWindowLimiter(
                            partitionKey: $"{apiKey}-chunks",
                            factory: _ => new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = 10000,
                                Window = TimeSpan.FromMinutes(1)
                            });
                    }

                    if (path.StartsWith("/api/files"))
                    {
                        // File operations: 1,000 per 3 minutes (moderate throughput for file metadata)
                        return RateLimitPartition.GetFixedWindowLimiter(
                            partitionKey: $"{apiKey}-files",
                            factory: _ => new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = 1000,
                                Window = TimeSpan.FromMinutes(3)
                            });
                    }

                    if (path.StartsWith("/api/tenant") || path.StartsWith("/api/admin"))
                    {
                        // Tenant and admin operations: 100 per minute (low throughput for configuration)
                        return RateLimitPartition.GetFixedWindowLimiter(
                            partitionKey: $"{apiKey}-config",
                            factory: _ => new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = 100,
                                Window = TimeSpan.FromMinutes(1)
                            });
                    }

                    // All other operations: 200 per minute (default)
                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: $"{apiKey}-default",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 200,
                            Window = TimeSpan.FromMinutes(1)
                        });
                });

                // Configure rate limiting options
                options.RejectionStatusCode = 429; // Too Many Requests
                options.OnRejected = async (context, token) =>
                {
                    context.HttpContext.Response.StatusCode = 429;
                    await context.HttpContext.Response.WriteAsync("Rate limit exceeded. Please try again later.", token);
                };
            });
        }
    }
}
