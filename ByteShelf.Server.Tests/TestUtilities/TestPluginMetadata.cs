using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Repositories;
using ByteShelfCommon;
using EasyReasy.Database;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>Writes plugins' documents straight to a server's tables, each call in a transaction of its own.</summary>
    internal static class TestPluginMetadata
    {
        public const string Plugin = "test-plugin";

        /// <summary>Sets a document about a file over whatever version is there.</summary>
        public static async Task<FilePluginMetadata> SetFileDocumentAsync(this IServiceProvider services, Guid fileId, string json, string plugin = Plugin)
        {
            return await InTransactionAsync(services, async (provider, session) =>
            {
                IFilePluginMetadataRepository repository = provider.GetRequiredService<IFilePluginMetadataRepository>();
                int? version = (await repository.GetAsync(plugin, fileId, session))?.Version;
                return (await repository.SetAsync(plugin, fileId, version, PluginDocument.Parse(json), DateTimeOffset.UtcNow, session))!;
            });
        }

        public static async Task RemoveFileDocumentAsync(this IServiceProvider services, Guid fileId, string plugin = Plugin)
        {
            await InTransactionAsync(services, async (provider, session) =>
            {
                IFilePluginMetadataRepository repository = provider.GetRequiredService<IFilePluginMetadataRepository>();
                int version = (await repository.GetAsync(plugin, fileId, session))!.Version;
                return (await repository.SetAsync(plugin, fileId, version, document: null, DateTimeOffset.UtcNow, session))!;
            });
        }

        public static async Task<string?> GetFileDocumentAsync(this IServiceProvider services, Guid fileId, string plugin = Plugin)
        {
            using IServiceScope scope = services.CreateScope();
            return (await scope.ServiceProvider.GetRequiredService<IFilePluginMetadataRepository>().GetAsync(plugin, fileId))?.Document?.Json;
        }

        /// <summary>Records a document about contents as a plugin's reader would; false when the server takes none about them.</summary>
        public static async Task<bool> RecordContentDocumentAsync(this IServiceProvider services, string sha256, string json, string plugin = Plugin, int version = 1)
        {
            return await InTransactionAsync(services, (provider, session) => provider.GetRequiredService<IContentPluginMetadataRepository>()
                .RecordAsync(ContentPluginMetadata.Of(plugin, sha256, version, DateTimeOffset.UtcNow, PluginDocument.Parse(json)), session));
        }

        public static async Task<string?> GetContentDocumentAsync(this IServiceProvider services, string sha256, string plugin = Plugin)
        {
            using IServiceScope scope = services.CreateScope();
            return (await scope.ServiceProvider.GetRequiredService<IContentPluginMetadataRepository>().GetAsync(plugin, sha256))?.Document?.Json;
        }

        /// <summary>Does something in a scope and transaction of its own.</summary>
        public static async Task<TResult> InTransactionAsync<TResult>(this IServiceProvider services, Func<IServiceProvider, IDbSession, Task<TResult>> write)
        {
            using IServiceScope scope = services.CreateScope();
            await using IDbTransactionSession session = await scope.ServiceProvider.GetRequiredService<IDbSessionFactory>().CreateSessionWithTransactionAsync();
            TResult result = await write(scope.ServiceProvider, session);
            await session.CommitAsync();
            return result;
        }
    }
}
