namespace ByteShelfCommon
{
    /// <summary>Carries a refresh token, to <c>POST /api/auth/refresh</c> to renew a session or <c>/logout</c> to end it.</summary>
    public sealed class RefreshTokenRequest
    {
        /// <summary>The session's current refresh token.</summary>
        public required string RefreshToken { get; init; }
    }
}
