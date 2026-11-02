using ByteShelf.Server.Shared;
using ByteShelf.Server.Tests.TestUtilities;
using Microsoft.Extensions.Hosting;

namespace ByteShelf.Server.Tests.Shared
{
    /// <summary>The address links point at, from <c>APP_BASE_URL</c>.</summary>
    public sealed class PublicAddressTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("  ")]
        public void Resolve_UnsetOutsideDevelopment_Throws(string? appBaseUrl)
        {
            Assert.Throws<InvalidOperationException>(() => PublicAddress.Resolve(appBaseUrl, HostEnvironments.Named(Environments.Production)));
        }

        [Fact]
        public void Resolve_UnsetInDevelopment_IsUnconfigured()
        {
            PublicAddress address = PublicAddress.Resolve(null, HostEnvironments.Named(Environments.Development));

            Assert.Null(address.Configured);
        }

        [Fact]
        public void Resolve_SetOutsideDevelopment_IsThatAddress()
        {
            PublicAddress address = PublicAddress.Resolve("https://shelf.example.com", HostEnvironments.Named(Environments.Production));

            Assert.Equal(new Uri("https://shelf.example.com/"), address.Configured);
        }
    }
}
