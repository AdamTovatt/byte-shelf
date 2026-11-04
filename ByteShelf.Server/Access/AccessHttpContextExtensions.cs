using ByteShelf.Server.Access.Auth;
using ByteShelf.Server.Access.Controllers;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Shared.Auth;
using ByteShelfCommon;

namespace ByteShelf.Server.Access
{
    /// <summary>Who is calling a shelf endpoint, and what it may do there, read from the request.</summary>
    public static class AccessHttpContextExtensions
    {
        private static readonly object AccessItem = new object();

        /// <summary>
        /// The key the request presented, or else the signed-in user. A request that presents a key
        /// which did not authenticate is refused even when it also carries a user's token, so a bad key
        /// never silently acts as someone else.
        /// </summary>
        /// <exception cref="AuthenticatedUserException">There is no valid caller.</exception>
        public static ShelfCaller GetShelfCaller(this HttpContext httpContext)
        {
            if (ShelfKeyAuthenticationHandler.PresentsKey(httpContext))
            {
                ShelfKey key = ShelfKeyAuthenticationHandler.GetKey(httpContext)
                    ?? throw new AuthenticatedUserException($"The {ShelfKeys.HeaderName} header does not hold a valid shelf key.");

                return ShelfCaller.ForKey(key);
            }

            return ShelfCaller.ForUser(httpContext.GetUserGuid(), httpContext.User.IsInRole(UserRoles.Admin));
        }

        /// <summary>The caller's access to the shelf in the route, as <see cref="ShelfAccessAttribute"/> resolved it.</summary>
        /// <exception cref="InvalidOperationException">The action is not marked <see cref="ShelfAccessAttribute"/>.</exception>
        public static ShelfAccess GetShelfAccess(this HttpContext httpContext)
        {
            return httpContext.Items[AccessItem] as ShelfAccess
                ?? throw new InvalidOperationException($"The action is not marked [{nameof(ShelfAccessAttribute)}].");
        }

        internal static void SetShelfAccess(this HttpContext httpContext, ShelfAccess access)
        {
            httpContext.Items[AccessItem] = access;
        }
    }
}
