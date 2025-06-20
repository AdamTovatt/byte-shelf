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

            // Configure authentication
            builder.Services.Configure<AuthenticationConfiguration>(builder.Configuration.GetSection("Authentication"));
            builder.Services.AddSingleton<AuthenticationConfiguration>(serviceProvider =>
            {
                AuthenticationConfiguration config = new AuthenticationConfiguration();
                builder.Configuration.GetSection("Authentication").Bind(config);
                return config;
            });

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
            builder.Services.AddSingleton<IFileStorageService>(serviceProvider =>
            {
                ILogger<FileStorageService>? logger = serviceProvider.GetService<ILogger<FileStorageService>>();
                return new FileStorageService(storagePath, logger);
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
