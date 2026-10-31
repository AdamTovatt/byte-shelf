using ByteShelf.Server.Replication.Repositories;
using ByteShelf.Server.Shared.Errors;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ByteShelf.Server.Replication.Controllers
{
    /// <summary>
    /// Refuses every action but GET and HEAD on a replica, unless it carries
    /// <see cref="AllowedOnReplicaAttribute"/>: a replica takes changes only from its primary. Applied to
    /// every controller, so a new action is refused there until somebody decides otherwise. Signing in,
    /// renewing and signing out are not controller actions and work on a replica as anywhere.
    /// </summary>
    public sealed class ReplicaReadOnlyFilter : IAsyncActionFilter
    {
        private readonly IReplicationSourceRepository _sourceRepository;

        public ReplicaReadOnlyFilter(IReplicationSourceRepository sourceRepository)
        {
            _sourceRepository = sourceRepository;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            string method = context.HttpContext.Request.Method;
            bool reads = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
            bool allowed = context.ActionDescriptor.EndpointMetadata.OfType<AllowedOnReplicaAttribute>().Any();

            if (!reads && !allowed && await _sourceRepository.GetAsync() != null)
            {
                throw new ReadOnlyReplicaException();
            }

            await next();
        }
    }
}
