using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Services;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;
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
        public async Task StartAsync_SettingsMissing_StartsWithoutUsers()
        {
            await SeedAsync(new InitialAdminSettings(null, null, null));

            Assert.Equal(0, await UserService.GetUserCountAsync());
        }

        private Task SeedAsync(InitialAdminSettings settings)
        {
            InitialAccountSeeder seeder = new InitialAccountSeeder(
                Services.GetRequiredService<IServiceScopeFactory>(), settings, NullLogger<InitialAccountSeeder>.Instance);

            return seeder.StartAsync(CancellationToken.None);
        }
    }
}
