namespace ByteShelf.Plugins
{
    /// <summary>What a derived file was made from, as its maker read the contents.</summary>
    /// <param name="Width">Its width in pixels, upright.</param>
    /// <param name="Height">Its height in pixels, upright.</param>
    /// <param name="MediaType">The media type its own bytes are, whatever the file was stored as.</param>
    public sealed record DerivedOriginal(int Width, int Height, string MediaType);
}
