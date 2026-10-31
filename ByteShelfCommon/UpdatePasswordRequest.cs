namespace ByteShelfCommon
{
    /// <summary>Changes the signed-in user's own password.</summary>
    public sealed class UpdatePasswordRequest
    {
        /// <summary>The password the user has now.</summary>
        public required string CurrentPassword { get; init; }

        /// <summary>The password the user wants.</summary>
        public required string NewPassword { get; init; }
    }
}
