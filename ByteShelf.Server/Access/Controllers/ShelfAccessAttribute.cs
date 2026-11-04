using ByteShelf.Server.Access.Auth;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Access.Services;
using ByteShelf.Server.Storage.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ByteShelf.Server.Access.Controllers
{
    /// <summary>
    /// Guards an action on <c>api/shelves/{shelf}/...</c>: requires a signed-in user or a key, resolves
    /// the <c>{shelf}</c> route value, and lets the action run only if the caller may do
    /// <see cref="Operation"/> there. A caller with no access is answered 404, one with too little 403.
    /// Actions read the outcome with <see cref="AccessHttpContextExtensions.GetShelfAccess"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ShelfAccessAttribute : TypeFilterAttribute, IAuthorizeData
    {
        public const string RouteValueName = "shelf";

        public ShelfAccessAttribute(ShelfOperation operation)
            : base(typeof(ShelfAccessFilter))
        {
            Operation = operation;
            Arguments = new object[] { operation };
        }

        public ShelfOperation Operation { get; }

        /// <inheritdoc />
        public string? Policy { get; set; } = ShelfKeyAuthentication.ShelfCallerPolicy;

        /// <inheritdoc />
        public string? Roles { get; set; }

        /// <inheritdoc />
        public string? AuthenticationSchemes { get; set; }

        private sealed class ShelfAccessFilter : IAsyncActionFilter
        {
            private readonly ShelfOperation _operation;
            private readonly IShelfService _shelfService;
            private readonly IShelfAccessResolver _accessResolver;

            public ShelfAccessFilter(ShelfOperation operation, IShelfService shelfService, IShelfAccessResolver accessResolver)
            {
                _operation = operation;
                _shelfService = shelfService;
                _accessResolver = accessResolver;
            }

            public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
            {
                string name = context.RouteData.Values[RouteValueName] as string
                    ?? throw new InvalidOperationException($"The route has no {{{RouteValueName}}} value.");

                ShelfCaller caller = context.HttpContext.GetShelfCaller();
                ShelfAccess access = await _accessResolver.ResolveAsync(caller, await _shelfService.GetByNameAsync(name));
                access.Require(_operation);

                context.HttpContext.SetShelfAccess(access);
                await next();
            }
        }
    }
}
