using ByteShelfCommon;
using ImageMagick;

namespace ByteShelf.Plugins.Images.Tests.TestUtilities
{
    internal static class ImageAssert
    {
        /// <param name="tolerance">How far each of red, green and blue may be off: by default what a lossy WebP or JPEG of a plain colour is.</param>
        public static void Color(IMagickColor<byte> expected, IMagickColor<byte> actual, int tolerance = 6, string? of = null)
        {
            Assert.True(
                Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance,
                $"Expected {expected}, found {actual} {of}.");
        }

        /// <summary>The colour near the top left corner of <paramref name="image"/>.</summary>
        public static void Color(IMagickColor<byte> expected, IMagickImage<byte> image, int tolerance = 6, string? of = null)
        {
            Color(expected, ColorAt(image, 2, 2), tolerance, of);
        }

        public static IMagickColor<byte> ColorAt(IMagickImage<byte> image, int x, int y)
        {
            using IPixelCollection<byte> pixels = image.GetPixels();
            return pixels.GetPixel(x, y).ToColor()!;
        }

        public static void Size(IMagickImage<byte> image, int width, int height)
        {
            Assert.Equal(((uint)width, (uint)height), (image.Width, image.Height));
        }

        /// <summary>Every kind was made, of an image of this size upright, whose quarters are of these colours upright.</summary>
        public static void AllOf(MadeFiles made, int width, int height, IReadOnlyList<IMagickColor<byte>> quarters)
        {
            MadeOf(made, MadeFiles.DeclaredKinds, width, height, quarters);
        }

        /// <summary>
        /// <paramref name="kinds"/> were made and nothing else, of an image of this size upright whose quarters are of these
        /// colours upright: each upright, and of its long side at most.
        /// </summary>
        public static void MadeOf(MadeFiles made, IReadOnlyList<DerivedKind> kinds, int width, int height, IReadOnlyList<IMagickColor<byte>> quarters)
        {
            Assert.Equal(kinds.Order(), made.Kinds);
            Assert.All(kinds, kind =>
            {
                Assert.Equal(kind is DerivedKind.Thumbnail or DerivedKind.Preview ? "image/webp" : "image/jpeg", made.MediaTypeOf(kind));
                using MagickImage image = made.Decode(kind);
                double scale = Math.Min(1, (double)LongSideOf(kind) / Math.Max(width, height));
                Size(image, (int)Math.Round(width * scale), (int)Math.Round(height * scale));
                Assert.True(image.Orientation is Orientation.Undefined or Orientation.TopLeft);
                int x = (int)image.Width / 4;
                int y = (int)image.Height / 4;
                Color(quarters[0], ColorAt(image, x, y));
                Color(quarters[1], ColorAt(image, x * 3, y));
                Color(quarters[2], ColorAt(image, x, y * 3));
                Color(quarters[3], ColorAt(image, x * 3, y * 3));
            });
        }

        private static int LongSideOf(DerivedKind kind)
        {
            return kind switch
            {
                DerivedKind.Thumbnail => 320,
                DerivedKind.Preview => 2048,
                DerivedKind.Full => ImageLimits.MaxJpegLongSide,
                DerivedKind.Agent => ImageLimits.AgentLongSide,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            };
        }
    }
}
