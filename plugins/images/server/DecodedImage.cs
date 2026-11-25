using ImageMagick;

namespace ByteShelf.Plugins.Images
{
    /// <summary>A decoded image, not turned upright yet, and the size of what it was decoded from, upright.</summary>
    internal sealed class DecodedImage : IDisposable
    {
        public DecodedImage(IMagickImage<byte> image, int uprightWidth, int uprightHeight)
        {
            Image = image;
            UprightWidth = uprightWidth;
            UprightHeight = uprightHeight;
        }

        public IMagickImage<byte> Image { get; }

        public int UprightWidth { get; }

        public int UprightHeight { get; }

        public void Dispose()
        {
            Image.Dispose();
        }
    }
}
