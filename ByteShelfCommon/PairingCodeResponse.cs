namespace ByteShelfCommon
{
    /// <summary>A pairing code to paste into a replica, usable once until it runs out.</summary>
    public sealed class PairingCodeResponse
    {
        /// <summary>How long a pairing code can be used.</summary>
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

        /// <summary>The code; it carries the primary's URL and a one-time secret.</summary>
        public required string Code { get; init; }

        /// <summary>When the code stops working.</summary>
        public required DateTimeOffset ExpiresAt { get; init; }
    }
}
