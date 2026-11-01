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

            // A key sent as the bearer token is the key scheme's; the session scheme would only fail to
            // read it as a token on every such request, and log that it did.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                JwtBearerEvents events = options.Events ?? new JwtBearerEvents();
                Func<MessageReceivedContext, Task> earlier = events.OnMessageReceived;
                events.OnMessageReceived = context =>
                {
                    if (ShelfKeyAuthenticationHandler.SendsKeyAsBearer(context.HttpContext))
                    {
                        context.NoResult();
                        return Task.CompletedTask;
                    }

                    return earlier(context);
                };
                options.Events = events;
            });

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
