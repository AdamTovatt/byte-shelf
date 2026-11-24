namespace ByteShelf.Plugins.Images
{
    /// <summary>What of an image is read at most: contents beyond it are refused before they are decoded.</summary>
    internal static class ImageLimits
    {
        public const long MaxSourceBytes = 200L * 1024 * 1024;

        /// <summary>Enough for a camera's, and for the 48 megapixels of a phone's. A small file can decode to any size.</summary>
        public const ulong MaxPixels = 120_000_000;

        /// <summary>
        /// What ImageMagick may take for pixels: an image of <see cref="MaxPixels"/> and one copy of it, as turning it takes.
        /// What decodes an AVIF or a HEIC takes memory of its own beside that, so the largest of those do not fit in what
        /// the process may have.
        /// </summary>
        public const ulong MaxMemoryBytes = 1024L * 1024 * 1024;

        /// <summary>The longest side a JPEG can have, as its library writes them.</summary>
        public const int MaxJpegLongSide = 65_500;

        /// <summary>A profile of a few primaries is some hundred bytes; one with tables, as a printer's, may be megabytes, in every thumbnail.</summary>
        public const int MaxProfileBytes = 64 * 1024;
    }
}
