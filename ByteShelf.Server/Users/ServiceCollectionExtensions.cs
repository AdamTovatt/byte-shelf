using ByteShelf.Server.Email.Services;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using ByteShelf.Server.Users.Services;
using EasyReasy.Auth;

namespace ByteShelf.Server.Users
{
    public static class ServiceCollectionExtensions
    {
        /// <param name="services">The service collection.</param>
        /// <param name="root">The root whose <c>keys/</c> folder holds the JWT secret.</param>
        /// <param name="appBaseUrl">The public URL reset links point at.</param>
        public static IServiceCollection AddUserServices(this IServiceCollection services, RootFolder root, string appBaseUrl)
        {
            services.TryAddTimeProvider();

            // Generated once into the root's keys/ folder, so a copied root keeps its sessions valid.
            string jwtSecret = RootKeyMaterial.GetOrCreate(root, RootKeyMaterial.JwtSecretFileName);
            services.AddEasyReasyAuth(jwtSecret, options => options.Issuer = AuthSetup.JwtIssuer);

            services.AddSingleton<IPasswordHasher, SecurePasswordHasher>();
            services.AddPasswordResetTokenHandler();

            // Library defaults on purpose: 30-day refresh tokens, 1-hour access tokens, and one token
            // family per device, so a user can be signed in on several at once.
            services.AddRefreshTokenService<RefreshTokenStore>();
            services.AddScoped<IRefreshClaimsResolver, CurrentRolesRefreshResolver>();
            services.AddScoped<IExpiredRefreshTokenCleanup>(provider =>
                (IExpiredRefreshTokenCleanup)provider.GetRequiredService<IRefreshTokenStore>());

            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IUserLoginRepository, UserLoginRepository>();
            services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();

            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IAuthRequestValidationService, AuthRequestValidationService>();
            services.AddScoped<IPasswordResetService, PasswordResetService>();
            services.AddScoped<IPasswordResetNotifier>(provider =>
                new EmailPasswordResetNotifier(provider.GetRequiredService<IEmailSender>(), appBaseUrl));

            services.AddSingleton(InitialAdminSettings.FromEnvironment());
            services.AddSingleton<ResetLinkOutbox>();
            services.AddHostedService<ResetLinkSender>();
            services.AddHostedService<InitialAccountSeeder>();
            services.AddHostedService<ExpiredTokenCleanupWorker>();

            return services;
        }
    }
}
