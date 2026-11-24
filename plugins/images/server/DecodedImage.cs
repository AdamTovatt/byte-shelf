using ImageMagick;

namespace ByteShelf.Plugins.Images
{
    /// <summary>A decoded image, not turned upright yet, and the size of what it was decoded from, upright.</summary>
    internal sealed record DecodedImage(IMagickImage<byte> Image, int UprightWidth, int UprightHeight) : IDisposable
    {
        public void Dispose()
        {
            Image.Dispose();
        }
    }
}
