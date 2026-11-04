namespace ByteShelfCommon
{
    /// <summary>
    /// Replaces a shelf's name and quota. Both are set: send the current name and quota to keep them,
    /// since leaving <see cref="QuotaBytes"/> out removes the quota.
    /// </summary>
    public sealed class UpdateShelfRequest
    {
        /// <summary>The shelf's name; see <see cref="ShelfName"/> for the rules. A new name changes the shelf's URLs.</summary>
        public required string Name { get; init; }

        /// <summary>How many bytes the shelf may hold; null (or left out) for no limit. A quota below what the shelf holds only stops new uploads.</summary>
        public long? QuotaBytes { get; init; }
    }
}
