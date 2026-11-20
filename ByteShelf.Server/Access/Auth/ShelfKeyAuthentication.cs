namespace ByteShelf.Server.Access.Auth
{
    /// <summary>The names that tie shelf keys into ASP.NET Core authentication.</summary>
    public static class ShelfKeyAuthentication
    {
        /// <summary>The scheme that reads a key from <see cref="ByteShelfCommon.ShelfKeys.HeaderName"/>.</summary>
        public const string Scheme = "ShelfKey";

        /// <summary>
        /// A signed-in user or a shelf key. Only the endpoints that require this policy run the key
        /// scheme, so a key can never reach the user endpoints.
        /// </summary>
        public const string ShelfCallerPolicy = "ShelfCaller";
    }
}
