using ByteShelfCommon.FileSystem;

namespace ByteShelf.Extensions
{
    /// <summary>
    /// Extension methods for HttpContext to easily access user and tenant information
    /// from the authentication middleware.
    /// </summary>
    public static class HttpContextExtensions
    {
        /// <summary>
        /// Gets the authenticated user ID from the HTTP context.
        /// </summary>
        /// <param name="httpContext">The HTTP context.</param>
        /// <returns>The user ID if available.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the user ID is not available in the context.</exception>
        public static string GetUserId(this HttpContext httpContext)
        {
            if (httpContext.Items.TryGetValue("UserId", out var userId) && userId is string userIdString)
            {
                return userIdString;
            }

            throw new InvalidOperationException("User ID not found in HTTP context. Ensure the authentication middleware is properly configured.");
        }

        /// <summary>
        /// Gets the authenticated tenant ID from the HTTP context.
        /// </summary>
        /// <param name="httpContext">The HTTP context.</param>
        /// <returns>The tenant ID if available.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the tenant ID is not available in the context.</exception>
        public static string GetTenantId(this HttpContext httpContext)
        {
            if (httpContext.Items.TryGetValue("TenantId", out var tenantId) && tenantId is string tenantIdString)
            {
                return tenantIdString;
            }

            throw new InvalidOperationException("Tenant ID not found in HTTP context. Ensure the authentication middleware is properly configured.");
        }

        /// <summary>
        /// Gets whether the authenticated user has admin privileges.
        /// </summary>
        /// <param name="httpContext">The HTTP context.</param>
        /// <returns>True if the user is an admin, false otherwise.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the admin status is not available in the context.</exception>
        public static bool GetIsAdmin(this HttpContext httpContext)
        {
            if (httpContext.Items.TryGetValue("IsAdmin", out var isAdmin) && isAdmin is bool isAdminBool)
            {
                return isAdminBool;
            }

            throw new InvalidOperationException("Admin status not found in HTTP context. Ensure the authentication middleware is properly configured.");
        }

        /// <summary>
        /// Gets the authenticated user configuration from the HTTP context.
        /// </summary>
        /// <param name="httpContext">The HTTP context.</param>
        /// <returns>The user configuration if available.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the user configuration is not available in the context.</exception>
        public static UserConfig GetUser(this HttpContext httpContext)
        {
            if (httpContext.Items.TryGetValue("User", out var user) && user is UserConfig userConfig)
            {
                return userConfig;
            }

            throw new InvalidOperationException("User configuration not found in HTTP context. Ensure the authentication middleware is properly configured.");
        }

        /// <summary>
        /// Gets the tenant configuration from the HTTP context.
        /// </summary>
        /// <param name="httpContext">The HTTP context.</param>
        /// <returns>The tenant configuration if available.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the tenant configuration is not available in the context.</exception>
        public static TenantConfig GetTenant(this HttpContext httpContext)
        {
            if (httpContext.Items.TryGetValue("Tenant", out var tenant) && tenant is TenantConfig tenantConfig)
            {
                return tenantConfig;
            }

            throw new InvalidOperationException("Tenant configuration not found in HTTP context. Ensure the authentication middleware is properly configured.");
        }

        /// <summary>
        /// Tries to get the authenticated user ID from the HTTP context.
        /// </summary>
        /// <param name="httpContext">The HTTP context.</param>
        /// <param name="userId">The user ID if available.</param>
        /// <returns>True if the user ID was found, false otherwise.</returns>
        public static bool TryGetUserId(this HttpContext httpContext, out string userId)
        {
            if (httpContext.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is string userIdString)
            {
                userId = userIdString;
                return true;
            }

            userId = string.Empty;
            return false;
        }

        /// <summary>
        /// Tries to get the authenticated tenant ID from the HTTP context.
        /// </summary>
        /// <param name="httpContext">The HTTP context.</param>
        /// <param name="tenantId">The tenant ID if available.</param>
        /// <returns>True if the tenant ID was found, false otherwise.</returns>
        public static bool TryGetTenantId(this HttpContext httpContext, out string tenantId)
        {
            if (httpContext.Items.TryGetValue("TenantId", out var tenantIdObj) && tenantIdObj is string tenantIdString)
            {
                tenantId = tenantIdString;
                return true;
            }

            tenantId = string.Empty;
            return false;
        }

        /// <summary>
        /// Tries to get the authenticated user configuration from the HTTP context.
        /// </summary>
        /// <param name="httpContext">The HTTP context.</param>
        /// <param name="user">The user configuration if available.</param>
        /// <returns>True if the user configuration was found, false otherwise.</returns>
        public static bool TryGetUser(this HttpContext httpContext, out UserConfig? user)
        {
            if (httpContext.Items.TryGetValue("User", out var userObj) && userObj is UserConfig userConfig)
            {
                user = userConfig;
                return true;
            }

            user = null;
            return false;
        }

        /// <summary>
        /// Tries to get the tenant configuration from the HTTP context.
        /// </summary>
        /// <param name="httpContext">The HTTP context.</param>
        /// <param name="tenant">The tenant configuration if available.</param>
        /// <returns>True if the tenant configuration was found, false otherwise.</returns>
        public static bool TryGetTenant(this HttpContext httpContext, out TenantConfig? tenant)
        {
            if (httpContext.Items.TryGetValue("Tenant", out var tenantObj) && tenantObj is TenantConfig tenantConfig)
            {
                tenant = tenantConfig;
                return true;
            }

            tenant = null;
            return false;
        }
    }
}