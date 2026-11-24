using ByteShelf.Server.Email;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Models;
using Microsoft.Extensions.Hosting;

namespace ByteShelf.Server.Tests.Shared
{
    /// <summary>What an instance is set up with, from the environment and without it.</summary>
    [Collection(nameof(ProcessStateCollection))]
    public sealed class ServerSettingsTests : IDisposable
    {
        private static readonly Dictionary<string, string> Set = new Dictionary<string, string>
        {
            ["APP_BASE_URL"] = "https://shelf.test/files",
            ["POSTMARK_API_KEY"] = "server-token",
            ["EMAIL_FROM_ADDRESS"] = "shelf@shelf.test",
            ["EMAIL_FROM_NAME"] = "Shelf",
            ["BYTESHELF_RESTORE_WINDOW_DAYS"] = "7",
            ["BYTESHELF_REPLICATION_ALERT_MINUTES"] = "90",
            ["INITIAL_ADMIN_EMAIL"] = "admin@shelf.test",
            ["INITIAL_ADMIN_PASSWORD"] = "password",
            ["INITIAL_ADMIN_DISPLAY_NAME"] = "First",
            ["BYTESHELF_TRUSTED_PROXIES"] = "10.0.0.1",
            ["BYTESHELF_PLUGINS"] = "/plugins",
        };

        private readonly Dictionary<string, string?> _before = Set.Keys.ToDictionary(name => name, Environment.GetEnvironmentVariable);

        public ServerSettingsTests()
        {
            foreach ((string name, string value) in Set)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        [Fact]
        public async Task FromEnvironment_HoldsEverySettingThere()
        {
            await using TestRoot root = TestRoot.Create();

            ServerSettings settings = ServerSettings.FromEnvironment(root.Root, HostEnvironments.Named(Environments.Production));

            Assert.Equal("https://shelf.test/files/", settings.PublicAddress.Configured?.AbsoluteUri);
            Assert.Equal(new PostmarkEmail("server-token", new MailSender("shelf@shelf.test", "Shelf")), settings.Email);
            Assert.Equal(TimeSpan.FromDays(7), settings.Storage.RestoreWindow);
            Assert.Equal((TimeSpan.FromMinutes(90), "https://shelf.test/files/"), (settings.Replication.AlertAfter, settings.Replication.PublicUrl));
            Assert.Equal(new InitialAdminSettings("admin@shelf.test", "password", "First"), settings.InitialAdmin);
            Assert.Equal("10.0.0.1", settings.TrustedProxies);
            Assert.Equal("/plugins", settings.PluginsPath);
        }

        [Fact]
        public async Task On_HoldsNothingOfTheEnvironments()
        {
            await using TestRoot root = TestRoot.Create();

            ServerSettings settings = ServerSettings.On(root.Root);

            Assert.Null(settings.PublicAddress.Configured);
            Assert.Same(EmailOptions.None, settings.Email);
            Assert.Equal(TimeSpan.FromDays(StorageOptions.DefaultRestoreWindowDays), settings.Storage.RestoreWindow);
            Assert.Equal(ReplicationOptions.DefaultAlertAfter, settings.Replication.AlertAfter);
            Assert.Equal(new InitialAdminSettings(null, null, null), settings.InitialAdmin);
            Assert.Null(settings.TrustedProxies);
            Assert.Null(settings.PluginsPath);
        }

        [Fact]
        public async Task On_ARootAgain_SignsWithTheSecretItHas()
        {
            await using TestRoot root = TestRoot.Create();

            Assert.Equal(ServerSettings.On(root.Root).JwtSecret, ServerSettings.On(root.Root).JwtSecret);
        }

        public void Dispose()
        {
            foreach ((string name, string? value) in _before)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
