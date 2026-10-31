namespace ByteShelfCommon
{
    /// <summary>Sets a new password with the token from a reset link.</summary>
    public sealed class CompletePasswordResetRequest
    {
        /// <summary>The token from the link.</summary>
        public required string Token { get; init; }

        /// <summary>The password the user wants.</summary>
        public required string NewPassword { get; init; }
    }
}
