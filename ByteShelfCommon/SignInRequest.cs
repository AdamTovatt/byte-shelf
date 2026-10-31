namespace ByteShelfCommon
{
    /// <summary>Signs a user in, at <c>POST /api/auth/login</c>.</summary>
    public sealed class SignInRequest
    {
        /// <summary>The user's email.</summary>
        public required string Username { get; init; }

        /// <summary>The user's password.</summary>
        public required string Password { get; init; }
    }
}
