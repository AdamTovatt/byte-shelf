using ByteShelf.Server.Mcp;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;

namespace ByteShelf.Server.Tests.Mcp
{
    /// <summary>Where the server calls its own API from its MCP tools, given the addresses it listens on.</summary>
    public sealed class SelfApiTests
    {
        [Theory]
        [InlineData("http://localhost:5190", "http://localhost:5190/")]
        [InlineData("http://127.0.0.1:5392", "http://127.0.0.1:5392/")]
        [InlineData("http://192.0.2.10:5190", "http://192.0.2.10:5190/")]
        [InlineData("http://[2001:db8::10]:5190", "http://[2001:db8::10]:5190/")]
        [InlineData("http://+:80", "http://127.0.0.1/")]
        [InlineData("http://*:8080", "http://127.0.0.1:8080/")]
        [InlineData("http://0.0.0.0:8080", "http://127.0.0.1:8080/")]
        [InlineData("http://[::]:8080", "http://[::1]:8080/")]
        public void SelfAddressFor_OneAddress_IsThatAddressFromThisMachine(string address, string expected)
        {
            Assert.Equal(new Uri(expected), SelfApi.SelfAddressFor(new[] { address }));
        }

        [Fact]
        public void SelfAddressFor_HttpsAndHttp_TakesHttp()
        {
            Assert.Equal(new Uri("http://127.0.0.1:5000/"), SelfApi.SelfAddressFor(new[] { "https://+:5001", "http://+:5000" }));
        }

        [Theory]
        [InlineData("https://+:5001")]
        [InlineData("http://unix:/run/byteshelf.sock")]
        public void SelfAddressFor_NoPlainHttpAddress_IsNone(string address)
        {
            Assert.Null(SelfApi.SelfAddressFor(new[] { address }));
        }

        [Fact]
        public void CreateClient_ListeningOnHttpsOnly_SaysTheToolsCannotRun()
        {
            SelfApi selfApi = new SelfApi(new ListeningServer("https://+:5001"), new PlainClientFactory());

            HttpRequestException exception = Assert.Throws<HttpRequestException>(() => selfApi.CreateClient());

            Assert.Contains("no plain-HTTP address", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void CreateClient_ListeningOnOneAddress_CallsItThere()
        {
            SelfApi selfApi = new SelfApi(new ListeningServer("https://+:5001", "http://192.0.2.10:5190"), new PlainClientFactory());

            using HttpClient client = selfApi.CreateClient();

            Assert.Equal(new Uri("http://192.0.2.10:5190/"), client.BaseAddress);
        }

        private sealed class ListeningServer : IServer
        {
            public ListeningServer(params string[] addresses)
            {
                ServerAddressesFeature feature = new ServerAddressesFeature();
                foreach (string address in addresses)
                {
                    feature.Addresses.Add(address);
                }

                Features.Set<IServerAddressesFeature>(feature);
            }

            public IFeatureCollection Features { get; } = new FeatureCollection();

            public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
                where TContext : notnull
            {
                throw new NotSupportedException();
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
            }
        }

        private sealed class PlainClientFactory : IHttpClientFactory
        {
            public HttpClient CreateClient(string name)
            {
                return new HttpClient();
            }
        }
    }
}
