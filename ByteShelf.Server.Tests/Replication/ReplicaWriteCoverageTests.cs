using ByteShelf.Server.Replication.Controllers;
using ByteShelf.Server.Tests.TestUtilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Replication
{
    /// <summary>
    /// A replica refuses every request that is not a read, but for the endpoints listed here. Every
    /// endpoint the server maps is looked at, controller actions and the rest alike: only a controller
    /// action passes <see cref="ReplicaReadOnlyFilter"/>, so anything else that takes a write (a minimal
    /// API, say) is let through on a replica until it is added here on purpose.
    /// </summary>
    public class ReplicaWriteCoverageTests
    {
        private static readonly string[] AllowedOnReplica =
        {
            // Actions marked [AllowedOnReplica]: they change nothing a replica holds, or only a replica does them.
            "POST api/shelves/{shelf}/download-link",
            "POST api/replication/promote",

            // Signing in, renewing and signing out work on a replica as anywhere.
            "POST api/auth/login",
            "POST api/auth/refresh",
            "POST api/auth/logout",

            // The MCP tools write through the server's own API, which the filter guards.
            "POST mcp/",

            // The web UI's fallback answers anything but a page with 404.
            "* {*path:nonfile}",
        };

        [Fact]
        public async Task EveryEndpointThatTakesWritesOnAReplica_IsListed()
        {
            await using ServerTestHost host = await ServerTestHost.StartAsync();
            List<string> passed = new List<string>();

            foreach (RouteEndpoint endpoint in host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
            {
                IReadOnlyList<string>? methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
                IEnumerable<string> writes = methods == null || methods.Count == 0
                    ? new[] { "*" }
                    : methods.Where(method => !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method));

                bool refused = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() != null
                    && endpoint.Metadata.GetMetadata<AllowedOnReplicaAttribute>() == null;

                if (!refused)
                {
                    passed.AddRange(writes.Select(method => $"{method} {endpoint.RoutePattern.RawText?.TrimStart('/')}"));
                }
            }

            Assert.Equal(AllowedOnReplica.Order(StringComparer.Ordinal), passed.Distinct().Order(StringComparer.Ordinal));
        }
    }
}
