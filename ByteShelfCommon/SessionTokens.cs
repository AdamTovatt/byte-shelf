namespace ByteShelfCommon
{
    /// <summary>What signing in or refreshing answers: a short-lived access token and the refresh token that renews it.</summary>
    public sealed class SessionTokens
    {
        /// <summary>The access token, sent as <c>Authorization: Bearer</c>.</summary>
        public required string Token { get; init; }

        /// <summary>When the access token stops working.</summary>
        public required DateTimeOffset ExpiresAt { get; init; }

        /// <summary>The token to refresh with; each one works once and is replaced by the next.</summary>
        public required string RefreshToken { get; init; }
    }
}
