namespace ByteShelfCommon
{
    /// <summary>Creates a key for a program, working on the shelf it is created on; it may be given other shelves later.</summary>
    public sealed class CreateShelfKeyRequest
    {
        /// <summary>A name saying which program holds it, at most <see cref="ShelfKeys.MaxNameLength"/> characters.</summary>
        public required string Name { get; init; }

        /// <summary>What the key may do on the shelf it is created on.</summary>
        public required ShelfKeyPermission Permission { get; init; }
    }
}
