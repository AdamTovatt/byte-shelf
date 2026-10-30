namespace ByteShelf.Server.Shared.RateLimiting
{
    /// <summary>
    /// Named rate-limit policy identifiers. Routes opt into a stricter tier via
    /// <c>[EnableRateLimiting(RateLimitPolicies.Strict)]</c>; unattributed routes are covered by
    /// the global per-caller limiter only.
    /// </summary>
    public static class RateLimitPolicies
    {
        public const string Strict = "strict";
        public const string VeryStrict = "very-strict";
    }
}
