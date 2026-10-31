namespace ByteShelfCommon
{
    /// <summary>Creates a key for a program to use one shelf with.</summary>
    public sealed class CreateShelfKeyRequest
    {
        /// <summary>A name saying which program holds it, at most <see cref="ShelfKeys.MaxNameLength"/> characters.</summary>
        public required string Name { get; init; }

        /// <summary>What the key may do on its shelf.</summary>
        public required ShelfKeyPermission Permission { get; init; }
    }
}
