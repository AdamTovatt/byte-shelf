using ByteShelf.Server.Access.Auth;
using ByteShelf.Server.Access.Repositories;
using ByteShelf.Server.Access.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace ByteShelf.Server.Access
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>Shelf grants, shelf keys, the key scheme and the policy shelf endpoints require.</summary>
        public static IServiceCollection AddAccessServices(this IServiceCollection services)
        {
            services.AddScoped<IShelfGrantRepository, ShelfGrantRepository>();
            services.AddScoped<IShelfKeyRepository, ShelfKeyRepository>();
            services.AddScoped<IShelfGrantService, ShelfGrantService>();
            services.AddScoped<IShelfKeyService, ShelfKeyService>();
            services.AddScoped<IShelfAccessResolver, ShelfAccessResolver>();

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, ShelfKeyAuthenticationHandler>(ShelfKeyAuthentication.Scheme, configureOptions: null);

            services.AddAuthorization(options =>
            {
                options.AddPolicy(ShelfKeyAuthentication.ShelfCallerPolicy, policy => policy
                    .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, ShelfKeyAuthentication.Scheme)
                    .RequireAuthenticatedUser());
            });

            return services;
        }
    }
}
