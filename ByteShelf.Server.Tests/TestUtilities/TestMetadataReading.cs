using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>Passes of the jobs that read contents on the server <c>services</c> belong to, run here rather than waiting for their workers.</summary>
    internal static class TestMetadataReading
    {
        /// <summary>One pass of metadata.reading.</summary>
        public static async Task<MetadataReadingSummary> ReadMetadataAsync(this IServiceProvider services)
        {
            using IServiceScope scope = services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<MetadataReading>().RunAsync(CancellationToken.None);
        }

        /// <summary>One pass of metadata.plugin_reading.</summary>
        public static async Task<PluginMetadataReadingSummary> ReadPluginMetadataAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
        {
            using IServiceScope scope = services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<PluginMetadataReading>().RunAsync(cancellationToken);
        }
    }
}
