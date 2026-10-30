namespace ByteShelf.Server.Shared.Auth
{
    /// <summary>
    /// The server-wide roles. <c>admin</c> creates shelves, users and keys and can reach every shelf.
    /// A user without it reaches only the shelves they have been given access to.
    /// </summary>
    public static class UserRole
    {
        public const string Admin = "admin";

        private static readonly string[] ValidRoles = { Admin };

        public static void Validate(string? role)
        {
            if (string.IsNullOrWhiteSpace(role) || !ValidRoles.Contains(role))
            {
                throw new ArgumentException($"'{role}' is not a role. Roles: {string.Join(", ", ValidRoles)}.");
            }
        }
    }
}
