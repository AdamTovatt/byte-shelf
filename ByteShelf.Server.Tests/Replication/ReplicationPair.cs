using System.Net.Http.Json;
using System.Text;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ByteShelf.Server.Tests.Replication
{
    /// <summary>
    /// Two whole servers in memory, a primary and a server whose calls to its primary reach that one,
    /// for the replication tests. The replica's background sync does not run: <see cref="SyncAsync"/>
    /// steps it, so a test decides exactly when the replica looks at the primary.
    /// </summary>
    public sealed class ReplicationPair : IAsyncDisposable
    {
        private ReplicationPair(ServerTestHost primary, ServerTestHost replica)
        {
            Primary = primary;
            Replica = replica;
        }

        public ServerTestHost Primary { get; }

        public ServerTestHost Replica { get; }

        /// <param name="wrapPrimary">Wraps the replica's way to the primary, such as to tamper with what it answers.</param>
        /// <param name="idleTimeout">How long the replica waits on a silent primary; the server's own by default.</param>
        /// <param name="pageSize">How many entries the replica asks for at a time; the server's own by default.</param>
        /// <param name="primaryClock">The clock the replica times its calls to the primary by; the real one by default.</param>
        public static async Task<ReplicationPair> StartAsync(
            Func<HttpMessageHandler, HttpMessageHandler>? wrapPrimary = null,
            TimeSpan? idleTimeout = null,
            int? pageSize = null,
            TimeProvider? primaryClock = null)
        {
            ServerTestHost primary = await ServerTestHost.StartAsync();
            return new ReplicationPair(primary, await StartServerToFollowAsync(primary, wrapPrimary, idleTimeout, pageSize, primaryClock));
        }

        /// <summary>
        /// A new server whose calls to a primary reach <paramref name="primary"/>, with its background sync
        /// off; the rest as <see cref="StartAsync"/>.
        /// </summary>
        public static Task<ServerTestHost> StartServerToFollowAsync(
            ServerTestHost primary,
            Func<HttpMessageHandler, HttpMessageHandler>? wrapPrimary = null,
            TimeSpan? idleTimeout = null,
            int? pageSize = null,
            TimeProvider? primaryClock = null)
        {
            return ServerTestHost.StartAsync(services =>
            {
                services.Remove(services.Single(service => service.ServiceType == typeof(IHostedService) && service.ImplementationType == typeof(ReplicaSyncWorker)));
                services
                    .AddHttpClient(PrimaryClient.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => wrapPrimary == null ? primary.CreateHandler() : wrapPrimary(primary.CreateHandler()));

                ReplicationOptions defaults = new ReplicationOptions { PublicUrl = "http://replica.test" };
                services.Replace(ServiceDescriptor.Singleton(new ReplicationOptions
                {
                    PublicUrl = defaults.PublicUrl,
                    IdleTimeout = idleTimeout ?? defaults.IdleTimeout,
                    PageSize = pageSize ?? defaults.PageSize,
                }));

                if (primaryClock != null)
                {
                    services.Replace(ServiceDescriptor.Scoped(provider => new PrimaryClient(
                        provider.GetRequiredService<IHttpClientFactory>(),
                        provider.GetRequiredService<ReplicationOptions>(),
                        primaryClock)));
                }
            });
        }

        /// <summary>Makes a code on the primary and pairs the replica with it.</summary>
        public async Task PairAsync()
        {
            (await TryPairAsync(await CreateCodeAsync())).EnsureSuccessStatusCode();
        }

        /// <summary>Asks the replica, as its own admin, to pair with <paramref name="code"/>.</summary>
        public async Task<HttpResponseMessage> TryPairAsync(string code)
        {
            return await Replica.AdminClient.PostAsJsonAsync("/api/replication/pair", new PairWithPrimaryRequest { Code = code, Name = "backup" });
        }

        public async Task<string> CreateCodeAsync()
        {
            HttpResponseMessage response = await Primary.AdminClient.PostAsync("/api/replication/codes", null);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<PairingCodeResponse>())!.Code;
        }

        /// <summary>Steps the replica's sync until it has caught up with the primary.</summary>
        public async Task SyncAsync()
        {
            for (int step = 0; step < 50; step++)
            {
                if (await SyncOnceAsync())
                {
                    return;
                }
            }

            throw new InvalidOperationException("The replica did not catch up in 50 steps.");
        }

        /// <summary>Takes one step of the replica's sync; answers whether it has caught up.</summary>
        public async Task<bool> SyncOnceAsync(CancellationToken cancellationToken = default)
        {
            using IServiceScope scope = Replica.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ReplicaSync>().SyncOnceAsync(cancellationToken);
        }

        /// <summary>A client on the replica signed in as the primary's admin, whose account the replica took.</summary>
        public Task<HttpClient> SignInOnReplicaAsync()
        {
            return Replica.CreateSignedInClientAsync(ServerTestHost.AdminEmail);
        }

        public async Task CreateShelfAsync(string name)
        {
            (await Primary.AdminClient.PostAsJsonAsync("/api/shelves", new CreateShelfRequest { Name = name })).EnsureSuccessStatusCode();
        }

        public async Task<ShelfFile> PutOnPrimaryAsync(string shelf, string path, string text)
        {
            using StringContent content = new StringContent(text, Encoding.UTF8, "text/plain");
            HttpResponseMessage response = await Primary.AdminClient.PutAsync($"/api/shelves/{shelf}/content?path={Uri.EscapeDataString(path)}", content);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ShelfFile>())!;
        }

        /// <summary>
        /// Encrypts <paramref name="file"/> in place on the primary, swapping its contents for an age file
        /// of <paramref name="text"/>: the server checks only that encrypted contents start as one.
        /// </summary>
        public async Task<ShelfFile> SwapOnPrimaryAsync(string shelf, ShelfFile file, string text)
        {
            using StringContent content = new StringContent($"age-encryption.org/v1\n{text}");
            HttpResponseMessage response = await Primary.AdminClient.PutAsync(
                $"/api/shelves/{shelf}/content/swap?id={file.Id}&sha256={file.Sha256}&encrypted=true", content);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ShelfFile>())!;
        }

        /// <summary>Deletes a file on the primary and empties the shelf's trash, so the file is purged there.</summary>
        public async Task PurgeOnPrimaryAsync(string shelf, string path)
        {
            (await Primary.AdminClient.DeleteAsync($"/api/shelves/{shelf}/files?path={Uri.EscapeDataString(path)}")).EnsureSuccessStatusCode();
            (await Primary.AdminClient.DeleteAsync($"/api/shelves/{shelf}/trash")).EnsureSuccessStatusCode();
        }

        /// <summary>The replication status <paramref name="client"/>'s server answers.</summary>
        public static async Task<ReplicationStatus> GetStatusAsync(HttpClient client)
        {
            return (await client.GetFromJsonAsync<ReplicationStatus>("/api/replication"))!;
        }

        public async ValueTask DisposeAsync()
        {
            await Replica.DisposeAsync();
            await Primary.DisposeAsync();
        }
    }
}
