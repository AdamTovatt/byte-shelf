using System.Threading.RateLimiting;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Shared.Json;
using ByteShelfCommon;
using Microsoft.AspNetCore.RateLimiting;

namespace ByteShelf.Server.Shared.RateLimiting
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddRateLimitingServices(this IServiceCollection services)
        {
            services.AddRateLimiter(options =>
            {
                // Every limiter is partitioned per caller (authenticated user, IP fallback): an
                // unpartitioned one would be a single bucket a single caller could empty for everyone.
                AddPartitionedPolicy(options, RateLimitPolicies.Strict, RateLimitPolicies.StrictPermitsPerMinute);
                AddPartitionedPolicy(options, RateLimitPolicies.VeryStrict, RateLimitPolicies.VeryStrictPermitsPerMinute);

                // Signing in checks a password hash, which is slow on purpose, and the progressive delay
                // starts over with every success; so sign-in and renewal share a strict bucket of their own.
                options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                    PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                        PerMinute(httpContext, RateLimitPolicies.GlobalPermitsPerMinute)),
                    PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                        AuthSetup.IsSignIn(httpContext)
                            ? PerMinute(httpContext, RateLimitPolicies.StrictPermitsPerMinute)
                            : RateLimitPartition.GetNoLimiter(string.Empty)));

                options.OnRejected = async (context, cancellationToken) =>
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    await context.HttpContext.Response.WriteAsJsonAsync(
                        new ErrorResponse { Message = "Too many requests; try again in a minute." },
                        JsonDefaults.CamelCase,
                        cancellationToken);
                };
            });

            return services;
        }

        private static void AddPartitionedPolicy(RateLimiterOptions options, string policyName, int permitsPerMinute)
        {
            options.AddPolicy(policyName, httpContext => PerMinute(httpContext, permitsPerMinute));
        }

        private static RateLimitPartition<string> PerMinute(HttpContext httpContext, int permitLimit)
        {
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: RateLimitPartitionKey.Resolve(httpContext),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
        }
    }
}
