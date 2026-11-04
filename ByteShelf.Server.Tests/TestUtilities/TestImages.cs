using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using SkiaSharp;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>Images for the previews /mcp's download_file makes, built and read through SkiaSharp's native library.</summary>
    internal static class TestImages
    {
        /// <summary>A PNG of smooth noise, which compresses about as a photo does.</summary>
        public static byte[] PhotoLikePng(int width, int height)
        {
            using SKImage image = PhotoLike(width, height);
            return Encode(image, SKEncodedImageFormat.Png, 100);
        }

        /// <summary>A PNG of noise in every pixel: fine detail that compresses worse than any photo.</summary>
        public static byte[] NoisePng(int width, int height)
        {
            using SKImage image = Noise(width, height);
            return Encode(image, SKEncodedImageFormat.Png, 100);
        }

        /// <summary>
        /// A JPEG stored <paramref name="width"/> by <paramref name="height"/>, its top-left quarter white
        /// and the rest noise (so it is well over 1 MiB), whose EXIF says to show it turned a quarter
        /// clockwise (orientation 6).
        /// </summary>
        public static byte[] JpegTurnedAQuarter(int width, int height)
        {
            using SKImage noise = Noise(width, height);
            using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height));
            surface.Canvas.DrawImage(noise, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
            using SKPaint white = new SKPaint { Color = SKColors.White };
            surface.Canvas.DrawRect(0, 0, width / 2f, height / 2f, white);
            using SKImage image = surface.Snapshot();
            byte[] jpeg = Encode(image, SKEncodedImageFormat.Jpeg, 100);
            return WithExifOrientation(jpeg, 6);
        }

        /// <summary>A PNG of photo-like noise on its left half and nothing, fully transparent, on its right.</summary>
        public static byte[] PhotoLikePngTransparentOnTheRight(int width, int height)
        {
            using SKImage photo = PhotoLike(width, height);
            using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.DrawImage(photo, new SKRect(0, 0, width / 2, height), new SKRect(0, 0, width / 2, height), new SKSamplingOptions(SKFilterMode.Nearest), null);
            using SKImage image = surface.Snapshot();
            return Encode(image, SKEncodedImageFormat.Png, 100);
        }

        /// <summary>The size of a JPEG, at <paramref name="quality"/>, of noise in every pixel.</summary>
        public static long NoiseJpegSize(int width, int height, int quality)
        {
            using SKImage image = Noise(width, height);
            return Encode(image, SKEncodedImageFormat.Jpeg, quality).Length;
        }

        /// <summary>
        /// <paramref name="length"/> bytes that start as a HEIC file does (an ISO media <c>ftyp</c> box
        /// of brand <c>heic</c>), which Skia does not decode on Linux.
        /// </summary>
        public static byte[] HeicHeaderOnly(int length)
        {
            byte[] heic = new byte[length];
            "\0\0\0\u0018ftypheic"u8.CopyTo(heic);
            return heic;
        }

        /// <summary>An uncompressed 24-bit BMP of noise, written by hand: Skia decodes BMP but does not write it.</summary>
        public static byte[] Bmp(int width, int height)
        {
            int rowBytes = ((width * 3) + 3) & ~3;
            byte[] bmp = new byte[54 + (rowBytes * height)];
            "BM"u8.CopyTo(bmp);
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2), bmp.Length);
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), 54);
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(14), 40);
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), width);
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), height);
            BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(26), 1);
            BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(28), 24);
            new Random(1).NextBytes(bmp.AsSpan(54));
            return bmp;
        }

        /// <summary>
        /// A PNG of <paramref name="width"/> by <paramref name="height"/> transparent pixels, written by
        /// hand: it is small on disk, and building it as an image would take the memory it decodes to.
        /// </summary>
        public static byte[] BlankRgbaPng(int width, int height)
        {
            using MemoryStream png = new MemoryStream();
            png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            byte[] header = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header, width);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
            header[8] = 8;
            header[9] = 6;
            WriteChunk(png, "IHDR", header);

            using (MemoryStream data = new MemoryStream())
            {
                using (ZLibStream zlib = new ZLibStream(data, CompressionLevel.Fastest, leaveOpen: true))
                {
                    // Each row is its filter byte, 0 (none), and four zero bytes a pixel.
                    byte[] row = new byte[1 + (width * 4)];
                    for (int y = 0; y < height; y++)
                    {
                        zlib.Write(row);
                    }
                }

                WriteChunk(png, "IDAT", data.ToArray());
            }

            WriteChunk(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        public static (int Width, int Height) SizeOf(byte[] image)
        {
            using SKCodec codec = SKCodec.Create(SKData.CreateCopy(image))!;
            return (codec.Info.Width, codec.Info.Height);
        }

        /// <summary>The pixel at the fractions <paramref name="x"/> and <paramref name="y"/> of the image's width and height.</summary>
        public static SKColor PixelAt(byte[] image, double x, double y)
        {
            using SKBitmap decoded = SKBitmap.Decode(image);
            return decoded.GetPixel((int)(decoded.Width * x), (int)(decoded.Height * y));
        }

        public static bool IsNearWhite(SKColor color)
        {
            return color.Red > 230 && color.Green > 230 && color.Blue > 230;
        }

        private static SKImage Noise(int width, int height)
        {
            byte[] pixels = new byte[width * height * 4];
            new Random(1).NextBytes(pixels);
            for (int alpha = 3; alpha < pixels.Length; alpha += 4)
            {
                pixels[alpha] = 255;
            }

            return SKImage.FromPixelCopy(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque), pixels);
        }

        /// <summary>Noise at a sixteenth of the size, drawn up smoothly to the size asked for.</summary>
        private static SKImage PhotoLike(int width, int height)
        {
            using SKImage coarse = Noise(Math.Max(1, width / 16), Math.Max(1, height / 16));
            using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            surface.Canvas.DrawImage(coarse, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Linear));
            return surface.Snapshot();
        }

        private static byte[] Encode(SKImage image, SKEncodedImageFormat format, int quality)
        {
            using SKData data = image.Encode(format, quality);
            return data.ToArray();
        }

        /// <summary><paramref name="jpeg"/> with an EXIF segment after its start marker that holds only <paramref name="orientation"/>.</summary>
        private static byte[] WithExifOrientation(byte[] jpeg, ushort orientation)
        {
            byte[] tiff = new byte[26];
            "II*\0"u8.CopyTo(tiff);
            BinaryPrimitives.WriteInt32LittleEndian(tiff.AsSpan(4), 8);
            BinaryPrimitives.WriteInt16LittleEndian(tiff.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(10), 0x0112);
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(12), 3);
            BinaryPrimitives.WriteInt32LittleEndian(tiff.AsSpan(14), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(18), orientation);
            BinaryPrimitives.WriteInt32LittleEndian(tiff.AsSpan(22), 0);

            byte[] payload = [.. "Exif\0\0"u8, .. tiff];
            byte[] segment = new byte[4 + payload.Length];
            segment[0] = 0xFF;
            segment[1] = 0xE1;
            BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(2 + payload.Length));
            payload.CopyTo(segment, 4);
            return [.. jpeg.AsSpan(0, 2), .. segment, .. jpeg.AsSpan(2)];
        }

        private static void WriteChunk(Stream png, string type, byte[] data)
        {
            byte[] length = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
            png.Write(length);
            byte[] typed = [.. Encoding.ASCII.GetBytes(type), .. data];
            png.Write(typed);
            byte[] crc = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typed));
            png.Write(crc);
        }

        /// <summary>PNG's CRC-32 (ISO 3309), over a chunk's type and data.</summary>
        private static uint Crc32(byte[] bytes)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte value in bytes)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
                }
            }

            return ~crc;
        }
    }
}
