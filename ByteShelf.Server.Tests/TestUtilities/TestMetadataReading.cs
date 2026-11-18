using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.TestUtilities
{
    internal static class TestMetadataReading
    {
        /// <summary>One pass of metadata.reading on the server <paramref name="services"/> belong to, run here rather than waiting for its worker.</summary>
        public static async Task<MetadataReadingSummary> ReadMetadataAsync(this IServiceProvider services)
        {
            using IServiceScope scope = services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<MetadataReading>().RunAsync(CancellationToken.None);
        }
    }
}
