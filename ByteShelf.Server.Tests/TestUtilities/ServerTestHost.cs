using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ByteShelf.Server.Email;
using ByteShelf.Server.Mcp;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Users.Services;
using ByteShelfCommon;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>
    /// The whole server, built by <see cref="Program.BuildApplication"/> on a root of the test's own and
    /// served in memory, with an admin signed in on <see cref="AdminClient"/> and a user without roles
    /// or shelf access on <see cref="MemberClient"/>.
    /// </summary>
    public sealed class ServerTestHost : IAsyncDisposable
    {
        public const string AdminEmail = "admin@byteshelf.test";
        public const string MemberEmail = "member@byteshelf.test";
        public const string Password = "password-for-tests";

        private readonly WebApplication _app;

        private ServerTestHost(WebApplication app, TestRoot root)
        {
            _app = app;
            TestRoot = root;
            AnonymousClient = app.GetTestClient();
            AdminClient = app.GetTestClient();
            MemberClient = app.GetTestClient();
        }

        public TestRoot TestRoot { get; }

        public IServiceProvider Services => _app.Services;

        public HttpClient AnonymousClient { get; }

        public HttpClient AdminClient { get; }

        public HttpClient MemberClient { get; }

        public Guid MemberId { get; private set; }

        /// <summary>A client of the in-memory server without credentials of its own, for ByteShelfClient to drive.</summary>
        public HttpClient CreateClient()
        {
            return _app.GetTestClient();
        }

        /// <summary>A handler sending requests to the in-memory server at <c>http://localhost/</c>, for code that makes its own clients.</summary>
        public HttpMessageHandler CreateHandler()
        {
            return _app.GetTestServer().CreateHandler();
        }

        /// <summary>A client sending <paramref name="secret"/> as its shelf key on every request.</summary>
        /// <summary>Creates a shelf as the admin and answers it.</summary>
        public async Task<ShelfInfo> CreateShelfAsync(string name)
        {
            HttpResponseMessage response = await AdminClient.PostAsJsonAsync("/api/shelves", new CreateShelfRequest { Name = name });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<ShelfInfo>())!;
        }

        public HttpClient CreateKeyClient(string secret)
        {
            HttpClient client = _app.GetTestClient();
            client.DefaultRequestHeaders.Add(ShelfKeys.HeaderName, secret);
            return client;
        }

        /// <param name="configureServices">Runs once the server's own services are registered, such as to route its calls to another host or replace a service.</param>
        public static async Task<ServerTestHost> StartAsync(Action<IServiceCollection>? configureServices = null)
        {
            TestRoot root = TestRoot.Create();

            // Development, so APP_BASE_URL may be left unset.
            WebApplication app = Program.BuildApplication(
                new[] { "--environment", "Development" },
                root.Root,
                builder =>
                {
                    builder.WebHost.UseTestServer();
                    builder.Logging.SetMinimumLevel(LogLevel.Warning);

                    // The MCP tools call the server's own API; in memory, the in-memory server is that.
                    builder.Services.AddHttpClient(SelfApi.HttpClientName, client => client.BaseAddress = new Uri("http://localhost/"))
                        .ConfigurePrimaryHttpMessageHandler(services => ((TestServer)services.GetRequiredService<IServer>()).CreateHandler());

                    // No public URL, whatever APP_BASE_URL the machine running the tests has; a test wanting one registers it.
                    builder.Services.AddSingleton(new PublicAddress(null));
                    configureServices?.Invoke(builder.Services);
                },
                EmailOptions.None);

            await app.StartAsync();
            ServerTestHost host = new ServerTestHost(app, root);

            using (IServiceScope scope = app.Services.CreateScope())
            {
                IUserService users = scope.ServiceProvider.GetRequiredService<IUserService>();
                await users.CreateUserAsync(AdminEmail, "Admin", Password, new[] { UserRoles.Admin });
                host.MemberId = (await users.CreateUserAsync(MemberEmail, "Member", Password)).Id;
            }

            await host.SignInAsync(host.AdminClient, AdminEmail);
            await host.SignInAsync(host.MemberClient, MemberEmail);
            return host;
        }

        public async ValueTask DisposeAsync()
        {
            AnonymousClient.Dispose();
            AdminClient.Dispose();
            MemberClient.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            await TestRoot.DisposeAsync();
        }

        /// <summary>A new client signed in as <paramref name="email"/> with <see cref="Password"/>.</summary>
        public async Task<HttpClient> CreateSignedInClientAsync(string email)
        {
            HttpClient client = _app.GetTestClient();
            await SignInAsync(client, email);
            return client;
        }

        private async Task SignInAsync(HttpClient client, string email)
        {
            HttpResponseMessage login = await AnonymousClient.PostAsJsonAsync("/api/auth/login", new { username = email, password = Password });
            login.EnsureSuccessStatusCode();
            using JsonDocument body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.RootElement.GetProperty("token").GetString());
        }
    }
}
