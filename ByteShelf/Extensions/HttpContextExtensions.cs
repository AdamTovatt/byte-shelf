namespace ByteShelf.Extensions
{
    /// <summary>
    /// Extension methods for <see cref="HttpContext"/> to support tenant operations.
    /// </summary>
    public static class HttpContextExtensions
    {
        /// <summary>
        /// Gets the tenant ID from the current request context.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns>The tenant ID.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the tenant ID is not available in the request context.</exception>
        /// <remarks>
        /// This method retrieves the tenant ID that was set by the authentication middleware.
        /// The tenant ID is extracted from the API key and stored in the request context.
        /// </remarks>
        public static string GetTenantId(this HttpContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (context.Items.TryGetValue("TenantId", out object? tenantIdObj) && tenantIdObj is string tenantId)
            {
                return tenantId;
            }

            throw new InvalidOperationException("Tenant ID not found in request context. This should not happen if authentication middleware is properly configured.");
        }

        /// <summary>
        /// Gets whether the current user has administrative privileges.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns><c>true</c> if the user is an admin; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        /// <remarks>
        /// This method retrieves the admin status that was set by the authentication middleware.
        /// Returns <c>false</c> if the admin status is not available in the request context.
        /// </remarks>
        public static bool IsAdmin(this HttpContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            return context.Items.TryGetValue("IsAdmin", out object? isAdminObj) && isAdminObj is bool isAdmin && isAdmin;
        }
    }
}