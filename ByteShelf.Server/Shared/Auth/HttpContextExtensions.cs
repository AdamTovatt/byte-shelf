using EasyReasy.Auth;

namespace ByteShelf.Server.Shared.Auth
{
    public static class HttpContextExtensions
    {
        /// <summary>
        /// Reads the authenticated user's id from the request context as a <see cref="Guid"/>.
        /// Throws <see cref="AuthenticatedUserException"/> (mapped to 401) when missing or malformed.
        /// </summary>
        public static Guid GetUserGuid(this HttpContext httpContext)
        {
            string? userId = httpContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                throw new AuthenticatedUserException("Missing authenticated user id.");
            }

            if (!Guid.TryParse(userId, out Guid userGuid))
            {
                throw new AuthenticatedUserException("Invalid authenticated user id.");
            }

            return userGuid;
        }
    }
}
