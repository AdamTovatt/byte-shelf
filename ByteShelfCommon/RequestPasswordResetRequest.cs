namespace ByteShelfCommon
{
    /// <summary>Asks for a password reset link to be sent to an email.</summary>
    public sealed class RequestPasswordResetRequest
    {
        /// <summary>The email of the account; the answer is the same whether or not one exists.</summary>
        public required string Email { get; init; }
    }
}
