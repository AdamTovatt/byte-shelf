using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ByteShelf.Server.Storage.Controllers
{
    /// <summary>
    /// Resolves the <c>{shelf}</c> route value of every action on the controller to its shelf before the
    /// action runs, answering 404 when there is none. Actions read it with
    /// <see cref="GetShelf(HttpContext)"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ShelfRouteAttribute : TypeFilterAttribute
    {
        public const string RouteValueName = "shelf";

        private static readonly object ShelfKey = new object();

        public ShelfRouteAttribute()
            : base(typeof(ResolveShelfFilter))
        {
        }

        public static Shelf GetShelf(HttpContext httpContext)
        {
            return httpContext.Items[ShelfKey] as Shelf
                ?? throw new InvalidOperationException($"The action's controller is not marked [{nameof(ShelfRouteAttribute)}].");
        }

        private sealed class ResolveShelfFilter : IAsyncActionFilter
        {
            private readonly IShelfService _shelfService;

            public ResolveShelfFilter(IShelfService shelfService)
            {
                _shelfService = shelfService;
            }

            public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
            {
                string name = context.RouteData.Values[RouteValueName] as string
                    ?? throw new InvalidOperationException($"The route has no {{{RouteValueName}}} value.");

                context.HttpContext.Items[ShelfKey] = await _shelfService.GetByNameAsync(name);
                await next();
            }
        }
    }
}
