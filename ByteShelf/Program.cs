using ByteShelf.Configuration;
using ByteShelf.Services;

namespace ByteShelf
{
    public class Program
    {
        public static void Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            // Add services to the container
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

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
            app.UseAuthorization();
            app.MapControllers();

            app.Run();
        }
    }
}
