namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>What the server asks a reader's process to read with.</summary>
    /// <param name="Version">The version the server knows: another plugin may be at the location by now.</param>
    internal sealed record ReadArguments(int Version);
}
