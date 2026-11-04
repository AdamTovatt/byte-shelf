namespace ByteShelf.Server.Access.Auth
{
    /// <summary>The names that tie shelf keys into ASP.NET Core authentication.</summary>
    public static class ShelfKeyAuthentication
    {
        /// <summary>The scheme that reads a key from <see cref="ByteShelfCommon.ShelfKeys.HeaderName"/>.</summary>
        public const string Scheme = "ShelfKey";

        /// <summary>
        /// The policy shelf endpoints require: a signed-in user or a key. Only these endpoints run the key
        /// scheme, so a key can never reach the user endpoints.
        /// </summary>
        public const string ShelfCallerPolicy = "ShelfCaller";
    }
}
