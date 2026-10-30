using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Users.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>
    /// The whole server, built by <see cref="Program.BuildApplication"/> on a root of the test's own and
    /// served in memory, with an admin signed in on <see cref="AdminClient"/> and a user without roles
    /// on <see cref="MemberClient"/>.
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

        public static async Task<ServerTestHost> StartAsync()
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
                });

            await app.StartAsync();
            ServerTestHost host = new ServerTestHost(app, root);

            using (IServiceScope scope = app.Services.CreateScope())
            {
                IUserService users = scope.ServiceProvider.GetRequiredService<IUserService>();
                await users.CreateUserAsync(AdminEmail, "Admin", Password, new[] { UserRole.Admin });
                await users.CreateUserAsync(MemberEmail, "Member", Password);
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

        private async Task SignInAsync(HttpClient client, string email)
        {
            HttpResponseMessage login = await AnonymousClient.PostAsJsonAsync("/api/auth/login", new { username = email, password = Password });
            login.EnsureSuccessStatusCode();
            using JsonDocument body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.RootElement.GetProperty("token").GetString());
        }
    }
}
