using ByteShelf.Server.Users.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>
    /// The server on real Kestrel over loopback, with an admin (<see cref="ServerTestHost.AdminEmail"/>), for what
    /// only Kestrel shows, which <see cref="ServerTestHost"/>'s in-memory server does not: its request-line limit,
    /// and its waiting for open requests as it stops.
    /// </summary>
    public sealed class KestrelTestHost : IAsyncDisposable
    {
        private readonly TestRoot _root;
        private readonly WebApplication _app;

        private KestrelTestHost(TestRoot root, WebApplication app)
        {
            _root = root;
            _app = app;
            Http = new HttpClient { BaseAddress = new Uri(app.Urls.Single() + "/") };
        }

        /// <summary>A client of the server without credentials of its own.</summary>
        public HttpClient Http { get; }

        /// <param name="configure">Changes the builder after the host's own setup, as adding a logging provider.</param>
        public static async Task<KestrelTestHost> StartAsync(Action<WebApplicationBuilder>? configure = null)
        {
            TestRoot root = TestRoot.Create();
            WebApplication app = Program.BuildApplication(
                Program.CreateBuilder(["--environment", "Development"]),
                ServerSettings.Open(root.Root),
                builder =>
                {
                    // Loopback only, on a port of the system's choosing.
                    builder.WebHost.UseUrls("http://127.0.0.1:0");
                    builder.Logging.SetMinimumLevel(LogLevel.Warning);
                    configure?.Invoke(builder);
                });
            await app.StartAsync();

            using (IServiceScope scope = app.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IUserService>().CreateUserAsync(ServerTestHost.AdminEmail, "Admin", ServerTestHost.Password, new[] { UserRoles.Admin });
            }

            return new KestrelTestHost(root, app);
        }

        /// <summary>Stops the server as a stop of the process does: Kestrel stops taking requests and waits for open ones.</summary>
        public Task StopAsync()
        {
            return _app.StopAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            await _root.DisposeAsync();
        }
    }
}
