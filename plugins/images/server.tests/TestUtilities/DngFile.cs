using System.Buffers.Binary;
using ImageMagick;

namespace ByteShelf.Plugins.Images.Tests.TestUtilities
{
    /// <summary>
    /// A camera's raw that LibRaw reads, made here as Adobe's DNG specification 1.4 has one: a linear raw of
    /// <see cref="Width"/> by <see cref="Height"/> pixels whose quarters are <see cref="TestImages.QuarterColors"/>, as it is
    /// stored, and what its camera made of it to show, if given, before it in the file. Its colours are those of sRGB
    /// without its curve; its numbers of red and blue are smaller than of green, as a sensor's are, so they come out as they
    /// went in only white balanced as shot. Read as a plain TIFF, which it begins as, they would not.
    /// </summary>
    internal static class DngFile
    {
        public const int Width = 160;

        public const int Height = 80;

        private const ushort DngVersion = 50706;
        private const ushort DngBackwardVersion = 50707;
        private const ushort UniqueCameraModel = 50708;
        private const ushort WhiteLevel = 50717;
        private const ushort ColorMatrix1 = 50721;
        private const ushort AsShotNeutral = 50728;
        private const ushort CalibrationIlluminant1 = 50778;

        /// <summary>DNG's photometric interpretation of a whole colour at each pixel, which nothing has to be made up of.</summary>
        private const ushort LinearRaw = 34892;

        /// <summary>CIE's D65, as DNG numbers illuminants.</summary>
        private const ushort D65 = 21;

        /// <summary>From CIE XYZ, of D65's white, to sRGB's colours without its curve: the camera's.</summary>
        private static readonly double[] XyzToCamera = [3.2406, -1.5372, -0.4986, -0.9689, 1.8758, 0.0415, 0.0557, -0.2040, 1.0570];

        /// <summary>What of each of red, green and blue the camera's numbers of white as shot are.</summary>
        private static readonly double[] Neutral = [0.5, 1, 0.75];

        /// <param name="orientation">EXIF's orientation, which it is stored as.</param>
        /// <param name="jpeg">A JPEG the camera made of it, as a smaller copy.</param>
        /// <param name="hasBitmap">Whether the camera made a bitmap of it, of 64 by 32 green pixels, as a smaller copy.</param>
        /// <param name="camera">What it says of the camera that took it.</param>
        /// <param name="saidWidth">The size it says it is of, when that is another.</param>
        public static byte[] Make(ushort orientation = 1, byte[]? jpeg = null, bool hasBitmap = false, string camera = "ByteShelf", int saidWidth = Width, int saidHeight = Height)
        {
            List<TiffFile.ImageDirectory> smaller = [.. jpeg == null ? [] : new[] { JpegDirectory(jpeg) }, .. hasBitmap ? [BitmapDirectory()] : Array.Empty<TiffFile.ImageDirectory>()];
            return TiffFile.Write(new TiffFile.ImageDirectory(
                [
                    TiffFile.Entry.Of(TiffFile.NewSubfileType, TiffFile.Long, 0),
                    TiffFile.Entry.Of(TiffFile.ImageWidth, TiffFile.Long, (uint)saidWidth),
                    TiffFile.Entry.Of(TiffFile.ImageLength, TiffFile.Long, (uint)saidHeight),
                    TiffFile.Entry.Of(TiffFile.BitsPerSample, TiffFile.Short, 16, 16, 16),
                    TiffFile.Entry.Of(TiffFile.Compression, TiffFile.Short, 1),
                    TiffFile.Entry.Of(TiffFile.PhotometricInterpretation, TiffFile.Short, LinearRaw),
                    TiffFile.Entry.Of(TiffFile.Make, camera),
                    TiffFile.Entry.Of(TiffFile.Model, camera),
                    TiffFile.Entry.StripsAt(),
                    TiffFile.Entry.Of(TiffFile.Orientation, TiffFile.Short, orientation),
                    TiffFile.Entry.Of(TiffFile.SamplesPerPixel, TiffFile.Short, 3),
                    TiffFile.Entry.Of(TiffFile.RowsPerStrip, TiffFile.Long, Height),
                    TiffFile.Entry.StripLengths(),
                    TiffFile.Entry.Of(TiffFile.PlanarConfiguration, TiffFile.Short, 1),
                    .. smaller.Count == 0 ? [] : new[] { TiffFile.Entry.SmallerCopiesAt(smaller.Count) },
                    TiffFile.Entry.Of(DngVersion, TiffFile.Byte, 1, 4, 0, 0),
                    TiffFile.Entry.Of(DngBackwardVersion, TiffFile.Byte, 1, 1, 0, 0),
                    TiffFile.Entry.Of(UniqueCameraModel, camera),
                    TiffFile.Entry.Of(WhiteLevel, TiffFile.Long, ushort.MaxValue),
                    TiffFile.Entry.OfFractions(ColorMatrix1, TiffFile.SignedRational, XyzToCamera),
                    TiffFile.Entry.OfFractions(AsShotNeutral, TiffFile.Rational, Neutral),
                    TiffFile.Entry.Of(CalibrationIlluminant1, TiffFile.Short, D65),
                ],
                Pixels(),
                smaller));
        }

        /// <summary>A JPEG of this size of the picture the raw is, as its camera makes one.</summary>
        public static byte[] Jpeg(int width, int height)
        {
            using MagickImage image = TestImages.Quarters(width, height);
            return TestImages.Jpeg(image);
        }

        private static TiffFile.ImageDirectory JpegDirectory(byte[] jpeg)
        {
            using MagickImage image = new MagickImage();
            image.Ping(jpeg);

            // Compressed as a JPEG (7), of luma and chroma (6).
            return SmallerCopy(image.Width, image.Height, 7, 6, jpeg);
        }

        private static TiffFile.ImageDirectory BitmapDirectory()
        {
            const int BitmapWidth = 64;
            const int BitmapHeight = 32;
            byte[] pixels = new byte[BitmapWidth * BitmapHeight * 3];
            for (int index = 1; index < pixels.Length; index += 3)
            {
                pixels[index] = byte.MaxValue;
            }

            // Not compressed (1), of red, green and blue (2).
            return SmallerCopy(BitmapWidth, BitmapHeight, 1, 2, pixels);
        }

        private static TiffFile.ImageDirectory SmallerCopy(uint width, uint height, ushort compression, ushort photometric, byte[] strip)
        {
            return new TiffFile.ImageDirectory(
                [
                    TiffFile.Entry.Of(TiffFile.NewSubfileType, TiffFile.Long, 1),
                    TiffFile.Entry.Of(TiffFile.ImageWidth, TiffFile.Long, width),
                    TiffFile.Entry.Of(TiffFile.ImageLength, TiffFile.Long, height),
                    TiffFile.Entry.Of(TiffFile.BitsPerSample, TiffFile.Short, 8, 8, 8),
                    TiffFile.Entry.Of(TiffFile.Compression, TiffFile.Short, compression),
                    TiffFile.Entry.Of(TiffFile.PhotometricInterpretation, TiffFile.Short, photometric),
                    TiffFile.Entry.StripsAt(),
                    TiffFile.Entry.Of(TiffFile.SamplesPerPixel, TiffFile.Short, 3),
                    TiffFile.Entry.Of(TiffFile.RowsPerStrip, TiffFile.Long, height),
                    TiffFile.Entry.StripLengths(),
                ],
                strip,
                []);
        }

        /// <summary>Each pixel's red, green and blue as the camera has them, in 16 bits each.</summary>
        private static byte[] Pixels()
        {
            byte[] pixels = new byte[Width * Height * 3 * 2];
            for (int index = 0; index < Width * Height; index++)
            {
                (int x, int y) = (index % Width, index / Width);
                MagickColor color = TestImages.QuarterColors[(y < Height / 2 ? 0 : 2) + (x < Width / 2 ? 0 : 1)];
                byte[] samples = [color.R, color.G, color.B];
                for (int sample = 0; sample < 3; sample++)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(pixels.AsSpan(((index * 3) + sample) * 2), (ushort)(samples[sample] * 257 * Neutral[sample]));
                }
            }

            return pixels;
        }
    }
}
