namespace ByteShelfCommon
{
    /// <summary>Replaces a user's roles.</summary>
    public sealed class UpdateRolesRequest
    {
        /// <summary>The user's roles from now on.</summary>
        public required IReadOnlyList<string> Roles { get; init; }
    }
}
