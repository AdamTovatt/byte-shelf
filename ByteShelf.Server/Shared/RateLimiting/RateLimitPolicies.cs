namespace ByteShelf.Server.Shared.RateLimiting
{
    /// <summary>
    /// Named rate-limit policy identifiers. Routes opt into a stricter tier via
    /// <c>[EnableRateLimiting(RateLimitPolicies.Strict)]</c>; unattributed routes are covered by
    /// the global per-caller limiter only. Signing in and renewing a session are held to
    /// <see cref="StrictPermitsPerMinute"/> by path, since the library that maps them takes no attribute.
    /// </summary>
    public static class RateLimitPolicies
    {
        public const string Strict = "strict";
        public const string VeryStrict = "very-strict";

        /// <summary>Requests per caller per minute across the whole app.</summary>
        public const int GlobalPermitsPerMinute = 1200;

        /// <summary>Requests per caller per minute to a <see cref="Strict"/> route, and to sign-in and renewal together.</summary>
        public const int StrictPermitsPerMinute = 60;

        /// <summary>Requests per caller per minute to a <see cref="VeryStrict"/> route.</summary>
        public const int VeryStrictPermitsPerMinute = 10;
    }
}
