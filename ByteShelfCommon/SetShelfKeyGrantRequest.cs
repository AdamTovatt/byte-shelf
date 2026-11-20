namespace ByteShelfCommon
{
    /// <summary>Gives a key a shelf, or changes what it may do there.</summary>
    public sealed class SetShelfKeyGrantRequest
    {
        /// <summary>What the key may do on the shelf.</summary>
        public required ShelfKeyPermission Permission { get; init; }
    }
}
