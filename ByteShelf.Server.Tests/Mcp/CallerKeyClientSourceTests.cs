using ByteShelf.Server.Mcp;
using Microsoft.AspNetCore.Http;

namespace ByteShelf.Server.Tests.Mcp
{
    /// <summary>Where a relative link the tools get back is put: the address the agent called /mcp at.</summary>
    public sealed class CallerKeyClientSourceTests
    {
        [Theory]
        [InlineData("http", "shelf.example.test", "", "http://shelf.example.test/")]
        [InlineData("https", "shelf.example.test:8443", "/files", "https://shelf.example.test:8443/files/")]
        public void CalledAt_IsTheSchemeHostAndPathPrefixAsCalled(string scheme, string host, string pathBase, string expected)
        {
            DefaultHttpContext context = new DefaultHttpContext();
            context.Request.Scheme = scheme;
            context.Request.Host = new HostString(host);
            context.Request.PathBase = new PathString(pathBase);

            Assert.Equal(new Uri(expected), CallerKeyClientSource.CalledAt(context.Request));
        }
    }
}
