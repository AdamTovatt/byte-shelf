namespace ByteShelfCommon
{
    /// <summary>Creates a shelf.</summary>
    public sealed class CreateShelfRequest
    {
        /// <summary>The shelf's name; see <see cref="ShelfName"/> for the rules.</summary>
        public required string Name { get; init; }

        /// <summary>How many bytes the shelf may hold; null for no limit.</summary>
        public long? QuotaBytes { get; init; }
    }
}
