using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Services;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ByteShelf.Server.Tests.Users
{
    public class InitialAccountSeederTests : ServiceTestBase
    {
        private IUserService UserService => Services.GetRequiredService<IUserService>();

        [Fact]
        public async Task StartAsync_NoUsers_CreatesTheAdminFromTheSettings()
        {
            await SeedAsync(new InitialAdminSettings("first@example.com", "admin-password", null));

            User admin = Assert.Single(await UserService.GetAllUsersAsync());
            Assert.Equal("first", admin.DisplayName);
            Assert.Equal(new[] { UserRoles.Admin }, admin.Roles);
            Assert.NotNull(await UserService.ValidateCredentialsAsync("first@example.com", "admin-password"));
        }

        [Fact]
        public async Task StartAsync_UsersExist_CreatesNoOne()
        {
            await UserService.CreateUserAsync("existing@example.com", "Existing", "password123");

            await SeedAsync(new InitialAdminSettings("first@example.com", "admin-password", "Admin"));

            Assert.Single(await UserService.GetAllUsersAsync());
        }

        [Fact]
        public async Task StartAsync_SettingsMissing_StartsWithoutUsersSayingHowToCreateTheFirstAdmin()
        {
            RecordingLoggerFactory logs = new RecordingLoggerFactory();

            await SeedAsync(new InitialAdminSettings(null, null, null), logs);

            Assert.Equal(0, await UserService.GetUserCountAsync());
            Assert.StartsWith("There are no users. Create the first admin", Assert.Single(logs.Messages), StringComparison.Ordinal);
        }

        /// <summary>A replica's users come with its primary's copy, so before it lands there are none to make or ask for.</summary>
        [Theory]
        [InlineData(null, null)]
        [InlineData("first@example.com", "admin-password")]
        public async Task StartAsync_OnAReplicaBeforeItsCopy_CreatesNoOneAndSaysItIsWaitingForThePrimary(string? email, string? password)
        {
            await TestReplicas.MarkAsReplicaAsync(Services, needsCopy: true);
            RecordingLoggerFactory logs = new RecordingLoggerFactory();

            await SeedAsync(new InitialAdminSettings(email, password, null), logs);

            Assert.Equal(0, await UserService.GetUserCountAsync());
            Assert.Contains("this server is a replica, waiting for its primary's copy", Assert.Single(logs.Messages), StringComparison.Ordinal);
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
            // The seeder's registrations, as the web host makes them; each test hands the seeder its own settings.
            services.AddInitialAdmin(new InitialAdminSettings(null, null, null));
        }

        private Task SeedAsync(InitialAdminSettings settings, ILoggerFactory? logs = null)
        {
            InitialAccountSeeder seeder = new InitialAccountSeeder(
                Services.GetRequiredService<IServiceScopeFactory>(), settings, (logs ?? NullLoggerFactory.Instance).CreateLogger<InitialAccountSeeder>());

            return seeder.StartAsync(CancellationToken.None);
        }
    }
}
