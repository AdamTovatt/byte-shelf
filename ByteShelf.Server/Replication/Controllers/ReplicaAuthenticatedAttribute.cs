using ByteShelf.Server.Replication.Models;
using ByteShelf.Server.Replication.Repositories;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Shared.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;

namespace ByteShelf.Server.Replication.Controllers
{
    /// <summary>
    /// Lets an action run only for a paired replica presenting its credential in
    /// <see cref="ReplicaCredentials.HeaderName"/>; anything else is answered 401. The replica reaches the
    /// action through <see cref="GetReplica"/>, never as a signed-in user, so no user check ever sees it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ReplicaAuthenticatedAttribute : TypeFilterAttribute
    {
        private const string ItemKey = "byteshelf.replica";

        public ReplicaAuthenticatedAttribute()
            : base(typeof(ReplicaAuthenticatedFilter))
        {
        }

        /// <summary>The replica the request was authenticated as.</summary>
        public static Replica GetReplica(HttpContext httpContext)
        {
            return httpContext.Items[ItemKey] as Replica
                ?? throw new InvalidOperationException($"The action has no [{nameof(ReplicaAuthenticatedAttribute)}].");
        }

        private sealed class ReplicaAuthenticatedFilter : IAsyncActionFilter
        {
            private readonly IReplicaRepository _replicaRepository;

            public ReplicaAuthenticatedFilter(IReplicaRepository replicaRepository)
            {
                _replicaRepository = replicaRepository;
            }

            public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
            {
                StringValues credentials = context.HttpContext.Request.Headers[ReplicaCredentials.HeaderName];
                Replica? replica = credentials.Count == 1 && !string.IsNullOrEmpty(credentials[0])
                    ? await _replicaRepository.GetByCredentialAsync(SecretHash.Sha256Hex(credentials[0]!))
                    : null;

                if (replica == null)
                {
                    throw new AuthenticatedUserException("This request needs the credential of a paired replica.");
                }

                context.HttpContext.Items[ItemKey] = replica;
                await next();
            }
        }
    }
}
