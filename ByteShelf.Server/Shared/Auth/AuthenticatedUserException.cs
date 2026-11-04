namespace ByteShelf.Server.Shared.Auth
{
    /// <summary>
    /// Thrown when the authenticated user's identity cannot be read from the request context.
    /// Mapped to 401 Unauthorized by <see cref="Middleware.ExceptionHandlingMiddleware"/>.
    /// </summary>
    public class AuthenticatedUserException : Exception
    {
        public AuthenticatedUserException(string message)
            : base(message)
        {
        }

        public AuthenticatedUserException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
