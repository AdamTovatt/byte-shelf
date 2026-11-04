namespace ByteShelfCommon
{
    /// <summary>Creates a user who signs in with an email and password.</summary>
    public sealed class CreateUserRequest
    {
        /// <summary>The email the user signs in with.</summary>
        public required string Email { get; init; }

        /// <summary>The name the user is shown by, 1 to 100 characters once trimmed.</summary>
        public required string DisplayName { get; init; }

        /// <summary>The user's first password.</summary>
        public required string Password { get; init; }

        /// <summary>The user's roles; none when left out.</summary>
        public IReadOnlyList<string>? Roles { get; init; }
    }
}
