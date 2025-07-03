using ByteShelf.Configuration;
using ByteShelf.Middleware;
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
                return config;
            });

            // Configure file storage
            string storagePath = builder.Configuration["StoragePath"] ?? "byte-shelf-storage";

            // Register tenant storage service
            builder.Services.AddSingleton<ITenantStorageService, TenantStorageService>();

            // Register tenant-aware file storage service
            builder.Services.AddSingleton<ITenantFileStorageService>(serviceProvider =>
            {
                ILogger<TenantFileStorageService>? logger = serviceProvider.GetService<ILogger<TenantFileStorageService>>();
                ITenantStorageService tenantStorageService = serviceProvider.GetRequiredService<ITenantStorageService>();
                return new TenantFileStorageService(storagePath, tenantStorageService, logger);
            });

            WebApplication app = builder.Build();

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
