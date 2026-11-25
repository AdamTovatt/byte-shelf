using System.Text.Json.Nodes;
using ImageMagick;

namespace ByteShelf.Plugins.Images
{
    /// <summary>
    /// Says what a viewer needs before it has any of an image: its <c>width</c> and <c>height</c> in pixels, upright, and a
    /// <c>placeholder</c>, a <c>data:</c> URL of a tiny copy to show blurred at that size until there is more. Of contents
    /// that are no image the plugin reads it says nothing.
    /// </summary>
    public sealed class ImageReader : IContentDocumentReader
    {
        /// <summary>Enough for colours and where they are, in some 100 bytes of WebP.</summary>
        internal const int PlaceholderLongSide = 16;

        private const uint PlaceholderQuality = 50;

        private readonly Codecs _codecs = Codecs.Prepare();

        public async Task<JsonObject?> ReadAsync(Stream contents)
        {
            if (await ImageSource.OpenAsync(_codecs, contents) is not { } source)
            {
                return null;
            }

            using DecodedImage decoded = source.Decode(PlaceholderLongSide);
            IMagickImage<byte> image = decoded.Image;
            Pictures.ShrinkUpright(image, PlaceholderLongSide);

            // A profile would be many times its size, and is not missed in a blur.
            Colors.ToSrgb(image);
            byte[] placeholder = Pictures.Encode(image, MagickFormat.WebP, PlaceholderQuality);
            return new JsonObject
            {
                ["width"] = decoded.UprightWidth,
                ["height"] = decoded.UprightHeight,
                ["placeholder"] = $"data:image/webp;base64,{Convert.ToBase64String(placeholder)}",
            };
        }
    }
}
