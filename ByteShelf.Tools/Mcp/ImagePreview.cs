namespace ByteShelf.Tools.Mcp
{
    /// <summary>
    /// An image too large, or of a kind a model cannot take, scaled down to a JPEG that download_file
    /// can answer: upright as a viewer shows it, on white where it is transparent, at most
    /// <see cref="LongSide"/> pixels on its long side and at most
    /// <see cref="ByteShelfMcpTools.MaxInlineImageBytes"/>, which it refuses to be built past. Made by
    /// the host's <see cref="IImagePreviews"/>, where it has one.
    /// </summary>
    /// <param name="Jpeg">The preview's bytes.</param>
    /// <param name="Width">The preview's width.</param>
    /// <param name="Height">The preview's height.</param>
    /// <param name="OriginalWidth">The image's own width, upright.</param>
    /// <param name="OriginalHeight">The image's own height, upright.</param>
    /// <param name="OriginalType">The media type the image's bytes are.</param>
    internal sealed record ImagePreview(byte[] Jpeg, int Width, int Height, int OriginalWidth, int OriginalHeight, string OriginalType)
    {
        /// <summary>
        /// The most pixels a preview has on its long side: the long side past which Claude's API scales
        /// an image down before the model sees it (Anthropic's vision documentation), so a larger
        /// preview would only cost more. Re-check it there when that changes.
        /// </summary>
        public const int LongSide = 1568;

        /// <summary>The largest image a preview is made from; a larger one is not read at all.</summary>
        public const long MaxSourceBytes = 32 * 1024 * 1024;

        public byte[] Jpeg { get; } = Jpeg.Length <= ByteShelfMcpTools.MaxInlineImageBytes
            ? Jpeg
            : throw new ArgumentException($"A preview is at most {ByteShelfMcpTools.MaxInlineImageBytes} bytes.", nameof(Jpeg));

        public int Width { get; } = Width is > 0 and <= LongSide ? Width : throw new ArgumentOutOfRangeException(nameof(Width));

        public int Height { get; } = Height is > 0 and <= LongSide ? Height : throw new ArgumentOutOfRangeException(nameof(Height));
    }
}
