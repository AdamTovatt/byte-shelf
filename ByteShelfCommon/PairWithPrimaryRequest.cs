namespace ByteShelfCommon
{
    /// <summary>Makes a server a replica: a pairing code made on the primary, and what the server is called there.</summary>
    public sealed class PairWithPrimaryRequest
    {
        /// <summary>The most characters a replica's name can have, as for any name shown in the web UI.</summary>
        public const int MaxNameLength = 100;

        /// <summary>The pairing code, as the primary gave it.</summary>
        public required string Code { get; init; }

        /// <summary>The replica's name on the primary, at most <see cref="MaxNameLength"/> characters.</summary>
        public required string Name { get; init; }
    }
}
