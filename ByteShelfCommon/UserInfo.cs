namespace ByteShelfCommon
{
    /// <summary>A user as the API answers it. Never carries credential material.</summary>
    public sealed class UserInfo
    {
        /// <summary>The user's id.</summary>
        public required Guid Id { get; init; }

        /// <summary>The email the user signs in with, unique without regard to case.</summary>
        public required string Email { get; init; }

        /// <summary>The name the user is shown by.</summary>
        public required string DisplayName { get; init; }

        /// <summary>The user's roles; <see cref="UserRoles.Admin"/> is the only one.</summary>
        public required IReadOnlyList<string> Roles { get; init; }
    }
}
