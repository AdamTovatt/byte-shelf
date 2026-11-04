using ByteShelf.Server.Tests.TestUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Shared
{
    /// <summary>The logging the server ships with.</summary>
    public sealed class LoggingConfigurationTests
    {
        [Fact]
        public async Task AppSettings_KeepRequestUrlsOutOfTheLog()
        {
            // ASP.NET Core logs every request's URL at Information, and download and reset links carry
            // their token in the URL.
            await using ServerTestHost host = await ServerTestHost.StartAsync();

            IConfiguration configuration = host.Services.GetRequiredService<IConfiguration>();

            Assert.Equal("Warning", configuration["Logging:LogLevel:Microsoft.AspNetCore"]);
        }
    }
}
