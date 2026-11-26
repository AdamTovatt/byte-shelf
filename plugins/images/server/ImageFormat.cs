using System.Buffers.Binary;
using System.Text;
using ImageMagick;

namespace ByteShelf.Plugins.Images
{
    /// <summary>
    /// A format the plugin reads, told by the contents' own first bytes and never by the type they are stored as,
    /// which is their uploader's word; of a TIFF's, what is a camera's raw is told by more of them (<see cref="CameraRaw"/>).
    /// ImageMagick is told it too, and so guesses nothing.
    /// </summary>
    internal sealed record ImageFormat(MagickFormat Magick)
    {
        /// <summary>The most of the contents' first bytes it takes to tell.</summary>
        public const int HeadBytes = 1024;

        private static readonly ImageFormat Png = new ImageFormat(MagickFormat.Png);
        private static readonly ImageFormat Gif = new ImageFormat(MagickFormat.Gif) { FirstFrameMayBePart = true };
        private static readonly ImageFormat WebP = new ImageFormat(MagickFormat.WebP);
        private static readonly ImageFormat Avif = new ImageFormat(MagickFormat.Avif);
        private static readonly ImageFormat Heic = new ImageFormat(MagickFormat.Heic);
        private static readonly ImageFormat Tiff = new ImageFormat(MagickFormat.Tiff) { MayBeCameraRaw = true };
        private static readonly ImageFormat Bmp = new ImageFormat(MagickFormat.Bmp);
        private static readonly ImageFormat Ico = new ImageFormat(MagickFormat.Ico) { LargestIsShown = true };

        /// <summary>The sizes of the headers a BMP has, after the 14 bytes of its file header.</summary>
        private static readonly uint[] BmpHeaderBytes = [12, 16, 40, 52, 56, 64, 108, 124];

        private static readonly string[] AvifBrands = ["avif", "avis"];
        private static readonly string[] HeicBrands = ["heic", "heix", "heim", "heis", "hevc", "hevx", "hevm", "hevs"];

        public static ImageFormat Jpeg { get; } = new ImageFormat(MagickFormat.Jpeg) { DecodesSmaller = true };

        /// <summary>
        /// A camera's raw, which LibRaw decodes (<see cref="CameraRaw"/>). ImageMagick decodes one at no smaller size, so it
        /// is decoded whole for every kind, as its camera's JPEG is not when that is large enough.
        /// </summary>
        public static ImageFormat Raw { get; } = new ImageFormat(MagickFormat.Dng) { IsCameraRaw = true };

        /// <summary>Whether it can be decoded at any number of eighths of its size, for less.</summary>
        public bool DecodesSmaller { get; private init; }

        /// <summary>Whether the first frame of an animation may be a part of what is shown.</summary>
        public bool FirstFrameMayBePart { get; private init; }

        /// <summary>Whether it is several sizes of one image, of which the largest is shown; of others, the first frame or page is.</summary>
        public bool LargestIsShown { get; private init; }

        /// <summary>Whether contents of its first bytes may be a camera's raw instead, as a CR2, a DNG, a NEF or an ARW begins as a TIFF.</summary>
        public bool MayBeCameraRaw { get; private init; }

        /// <summary>Whether it is a camera's raw.</summary>
        public bool IsCameraRaw { get; private init; }

        /// <summary>The format of the contents whose first bytes <paramref name="head"/> is; null when it is none of these.</summary>
        public static ImageFormat? Of(ReadOnlySpan<byte> head)
        {
            return head switch
            {
                [0xFF, 0xD8, 0xFF, ..] => Jpeg,
                [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, ..] => Png,
                [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'7' or (byte)'9', (byte)'a', ..] => Gif,
                [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => WebP,

                // 43 is a BigTIFF.
                [(byte)'I', (byte)'I', 42 or 43, 0, ..] or [(byte)'M', (byte)'M', 0, 42 or 43, ..] => Tiff,
                [(byte)'B', (byte)'M', ..] => IsBmp(head) ? Bmp : null,

                // Before an icon's, which a first box of 256 bytes (00 00 01 00) would also match.
                [_, _, _, _, (byte)'f', (byte)'t', (byte)'y', (byte)'p', ..] => OfBrands(head),
                [0, 0, 1, 0, not 0, _, ..] or [0, 0, 1, 0, 0, not 0, ..] => Ico,
                _ => null,
            };
        }

        private static bool IsBmp(ReadOnlySpan<byte> head)
        {
            return head.Length >= 18 && BmpHeaderBytes.Contains(BinaryPrimitives.ReadUInt32LittleEndian(head[14..]));
        }

        /// <summary>The format a file made of boxes says it is, in its first: one brand, its version, and the brands it is compatible with.</summary>
        private static ImageFormat? OfBrands(ReadOnlySpan<byte> head)
        {
            int length = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(head), head.Length);
            List<string> brands = [];
            for (int offset = 8; offset + 4 <= length; offset += 4)
            {
                if (offset != 12)
                {
                    brands.Add(Encoding.Latin1.GetString(head.Slice(offset, 4)));
                }
            }

            return brands.Any(AvifBrands.Contains) ? Avif : brands.Any(HeicBrands.Contains) ? Heic : null;
        }
    }
}
