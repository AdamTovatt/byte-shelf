namespace ByteShelfCommon
{
    /// <summary>A replica promoted to a primary of its own.</summary>
    public sealed class PromoteResponse
    {
        /// <summary>Whether its old primary heard it left; if not, it stays listed there until removed.</summary>
        public required bool PrimaryTold { get; init; }
    }
}
