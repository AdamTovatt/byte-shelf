using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Users.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>
    /// First-run seeding: creates the admin account from <see cref="InitialAdminSettings"/> — but only
    /// while there are no users, so the settings are inert on every later boot and can be removed once
    /// the admin exists. The display name falls back to the email's local part. The settings are
    /// optional: without them it logs how to create the first admin with <see cref="UserCommand"/>.
    /// </summary>
    public class InitialAccountSeeder : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly InitialAdminSettings _settings;
        private readonly ILogger<InitialAccountSeeder> _logger;

        public InitialAccountSeeder(IServiceScopeFactory scopeFactory, InitialAdminSettings settings, ILogger<InitialAccountSeeder> logger)
        {
            _scopeFactory = scopeFactory;
            _settings = settings;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IUserService userService = scope.ServiceProvider.GetRequiredService<IUserService>();

            int userCount = await userService.GetUserCountAsync();
            if (userCount > 0)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_settings.Email) || string.IsNullOrWhiteSpace(_settings.Password))
            {
                // Not fatal: a replica receives its users from its primary, and the first admin can be
                // created beside the running server.
                _logger.LogWarning(
                    "There are no users. Create the first admin on this machine with BYTESHELF_ROOT set to this root: "
                    + "{Command}, giving --admin (it asks for the password), "
                    + "or set INITIAL_ADMIN_EMAIL and INITIAL_ADMIN_PASSWORD and restart.",
                    UserCommand.CreateUserUsage);
                return;
            }

            string displayName = string.IsNullOrWhiteSpace(_settings.DisplayName)
                ? _settings.Email.Split('@')[0]
                : _settings.DisplayName;

            await userService.CreateUserAsync(_settings.Email, displayName, _settings.Password, new[] { UserRoles.Admin });
            _logger.LogInformation("Initial admin account created for {Email}.", _settings.Email);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
