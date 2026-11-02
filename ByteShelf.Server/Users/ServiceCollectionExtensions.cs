using ByteShelf.Server.Email.Services;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Users.Models;
using ByteShelf.Server.Users.Repositories;
using ByteShelf.Server.Users.Services;
using EasyReasy.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace ByteShelf.Server.Users
{
    public static class ServiceCollectionExtensions
    {
        /// <param name="services">The service collection.</param>
        /// <param name="root">The root whose <c>keys/</c> folder holds the JWT secret.</param>
        /// <remarks>Reset links are made on the registered <see cref="PublicAddress"/>.</remarks>
        public static IServiceCollection AddUserServices(this IServiceCollection services, RootFolder root)
        {
            services.TryAddTimeProvider();

            // Generated once into the root's keys/ folder, so a copied root keeps its sessions valid.
            string jwtSecret = RootKeyMaterial.GetOrCreate(root, RootKeyMaterial.JwtSecretFileName);
            services.AddEasyReasyAuth(jwtSecret, options => options.Issuer = AuthSetup.JwtIssuer);

            // Every request of a signed-in user sees the roles they have now, not the token's.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Events ??= new JwtBearerEvents();
                CurrentUserTokenValidation.AddTo(options.Events);
            });

            services.AddSingleton<IPasswordHasher, SecurePasswordHasher>();
            services.AddPasswordResetTokenHandler();

            // Library defaults on purpose: 30-day refresh tokens, 1-hour access tokens, and one token
            // family per device, so a user can be signed in on several at once.
            services.AddRefreshTokenService<RefreshTokenStore>();
            services.AddScoped<IRefreshClaimsResolver, CurrentRolesRefreshResolver>();
            services.AddScoped<IExpiredRefreshTokenCleanup>(provider =>
                (IExpiredRefreshTokenCleanup)provider.GetRequiredService<IRefreshTokenStore>());
            services.AddScoped<ISessionStampStore>(provider =>
                (ISessionStampStore)provider.GetRequiredService<IRefreshTokenStore>());

            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IUserLoginRepository, UserLoginRepository>();
            services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();

            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IAuthRequestValidationService, AuthRequestValidationService>();
            services.AddScoped<IPasswordResetService, PasswordResetService>();
            services.AddScoped<IPasswordResetNotifier, EmailPasswordResetNotifier>();

            services.AddSingleton(InitialAdminSettings.FromEnvironment());
            services.AddSingleton<ResetLinkOutbox>();
            services.AddHostedService<ResetLinkSender>();
            services.AddHostedService<InitialAccountSeeder>();
            services.AddHostedService<ExpiredTokenCleanupWorker>();

            return services;
        }
    }
}
