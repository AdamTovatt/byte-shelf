namespace ByteShelfCommon
{
    /// <summary>Who made a link, whose access it works with.</summary>
    public sealed class LinkMaker
    {
        /// <summary>Whether a user or a key made it.</summary>
        public required LinkMakerKind Kind { get; init; }

        /// <summary>The user's or the key's id.</summary>
        public required Guid Id { get; init; }

        /// <summary>The user's display name, or the key's name.</summary>
        public required string Name { get; init; }

        /// <summary>Whether it is whoever asked for the list.</summary>
        public required bool IsCaller { get; init; }
    }
}
