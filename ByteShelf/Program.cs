using ByteShelf.Configuration;
using ByteShelf.Middleware;
using ByteShelf.Resources;
using ByteShelf.Services;

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
            builder.Services.AddSingleton<IStorageService, StorageService>();

            // Register file storage service
            builder.Services.AddSingleton<IFileStorageService>(serviceProvider =>
            {
                ILogger<FileStorageService>? logger = serviceProvider.GetService<ILogger<FileStorageService>>();
                IStorageService storageService = serviceProvider.GetRequiredService<IStorageService>();
                return new FileStorageService(storagePath, storageService, logger);
            });

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

            app.UseAuthorization();
            app.MapControllers();

            app.Run();
        }
    }
}
