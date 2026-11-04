namespace ByteShelfCommon
{
    /// <summary>Gives a user access to a shelf, or changes the access they have.</summary>
    public sealed class SetShelfAccessRequest
    {
        /// <summary>What the user may do on the shelf.</summary>
        public required ShelfAccessPermission Permission { get; init; }
    }
}
