using System.Threading.RateLimiting;
using ByteShelf.Server.Shared.Json;
using ByteShelfCommon;
using Microsoft.AspNetCore.RateLimiting;

namespace ByteShelf.Server.Shared.RateLimiting
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>Requests per caller per minute across the whole app.</summary>
        private const int GlobalPermitsPerMinute = 1200;

        public static IServiceCollection AddRateLimitingServices(this IServiceCollection services)
        {
            services.AddRateLimiter(options =>
            {
                // Every limiter is partitioned per caller (authenticated user, IP fallback): an
                // unpartitioned one would be a single bucket a single caller could empty for everyone.
                AddPartitionedPolicy(options, RateLimitPolicies.Strict, permitsPerMinute: 60);
                AddPartitionedPolicy(options, RateLimitPolicies.VeryStrict, permitsPerMinute: 10);

                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                    PerMinute(httpContext, GlobalPermitsPerMinute));

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
