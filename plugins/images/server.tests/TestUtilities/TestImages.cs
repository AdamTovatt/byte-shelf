using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;
using ImageMagick;

namespace ByteShelf.Plugins.Images.Tests.TestUtilities
{
    /// <summary>Images, made here.</summary>
    internal static class TestImages
    {
        /// <summary>Those <see cref="Of"/> makes: every one the plugin reads but HEIC, which ImageMagick cannot write (<see cref="HeicFile"/>).</summary>
        public static TheoryData<string> Formats { get; } = ["jpeg", "png", "gif", "webp", "avif", "tiff", "bmp", "x-icon"];

        /// <summary>The colours of the quarters of <see cref="Quarters"/>, as it is stored: top left, top right, bottom left, bottom right.</summary>
        public static IReadOnlyList<MagickColor> QuarterColors { get; } = [MagickColors.Red, MagickColors.Lime, MagickColors.Blue, MagickColors.White];

        public static MagickImage Plain(int width, int height, byte red, byte green, byte blue)
        {
            return Drawn(width, height, (_, _) => (red, green, blue, 255));
        }

        /// <summary>Four quarters, each of its colour.</summary>
        public static MagickImage Quarters(int width, int height)
        {
            return Drawn(width, height, (x, y) =>
            {
                MagickColor color = QuarterColors[(y < height / 2 ? 0 : 2) + (x < width / 2 ? 0 : 1)];
                return (color.R, color.G, color.B, 255);
            });
        }

        /// <summary>The left half transparent, the right half red.</summary>
        public static MagickImage HalfTransparent(int width, int height)
        {
            return Drawn(width, height, (x, _) => x < width / 2 ? ((byte)0, (byte)0, (byte)0, (byte)0) : ((byte)255, (byte)0, (byte)0, (byte)255));
        }

        public static MagickImage Noise(int width, int height)
        {
            Random random = new Random(1);
            return Drawn(width, height, (_, _) => ((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255));
        }

        public static MagickImage Drawn(int width, int height, Func<int, int, (byte Red, byte Green, byte Blue, byte Alpha)> colorAt)
        {
            byte[] pixels = new byte[width * height * 4];
            for (int index = 0; index < width * height; index++)
            {
                (pixels[index * 4], pixels[(index * 4) + 1], pixels[(index * 4) + 2], pixels[(index * 4) + 3]) = colorAt(index % width, index / width);
            }

            MagickImage image = new MagickImage(pixels, new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.RGBA));
            if (image.IsOpaque)
            {
                image.Alpha(AlphaAction.Off);
            }

            return image;
        }

        /// <summary>Contents of the format named, as it is in <c>image/…</c>.</summary>
        public static byte[] Of(string format, MagickImage image)
        {
            return format switch
            {
                "jpeg" => Jpeg(image),
                "png" => image.ToByteArray(MagickFormat.Png),
                "gif" => image.ToByteArray(MagickFormat.Gif),
                "webp" => WebP(image),
                "avif" => image.ToByteArray(MagickFormat.Avif),
                "tiff" => image.ToByteArray(MagickFormat.Tiff),
                "bmp" => image.ToByteArray(MagickFormat.Bmp),
                "x-icon" => Ico(image.ToByteArray(MagickFormat.Png)),
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
            };
        }

        public static byte[] Jpeg(MagickImage image)
        {
            image.Quality = 98;
            return image.ToByteArray(MagickFormat.Jpeg);
        }

        /// <summary>An icon of <paramref name="pngs"/>, in their order.</summary>
        public static byte[] Ico(params byte[][] pngs)
        {
            using MemoryStream file = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(file);
            writer.Write([0, 0, 1, 0]);
            writer.Write((short)pngs.Length);
            int offset = 6 + (16 * pngs.Length);
            foreach (byte[] png in pngs)
            {
                writer.Write([0, 0, 0, 0, 1, 0, 32, 0]);
                writer.Write(png.Length);
                writer.Write(offset);
                offset += png.Length;
            }

            foreach (byte[] png in pngs)
            {
                writer.Write(png);
            }

            return file.ToArray();
        }

        /// <summary>A black PNG, of few bytes for its size.</summary>
        public static byte[] BlackPng(int width, int height)
        {
            using MemoryStream compressed = new MemoryStream();
            using (ZLibStream data = new ZLibStream(compressed, CompressionLevel.Optimal))
            {
                // A row is its filter and a bit a pixel.
                byte[] row = new byte[1 + ((width + 7) / 8)];
                for (int y = 0; y < height; y++)
                {
                    data.Write(row);
                }
            }

            byte[] header = [0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0];
            BinaryPrimitives.WriteInt32BigEndian(header, width);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
            return [0x89, .. Encoding.ASCII.GetBytes("PNG\r\n"), 0x1A, (byte)'\n', .. PngChunk("IHDR", header), .. PngChunk("IDAT", compressed.ToArray()), .. PngChunk("IEND", [])];
        }

        /// <summary>A TIFF that is transparent all over, with four bytes a pixel, of few bytes for its size.</summary>
        public static byte[] BlankTiff(int width, int height, ushort orientation)
        {
            const int RowsPerStrip = 100;
            using MemoryStream compressed = new MemoryStream();
            using (ZLibStream data = new ZLibStream(compressed, CompressionLevel.Optimal))
            {
                data.Write(new byte[width * 4 * RowsPerStrip]);
            }

            // Every strip is the one there is.
            byte[] strip = compressed.ToArray();
            int strips = height / RowsPerStrip;
            (ushort Tag, ushort Type, int Count, int Value)[] entries =
            [
                (256, 4, 1, width),
                (257, 4, 1, height),
                (258, 3, 4, 0),
                (259, 3, 1, 8),
                (262, 3, 1, 2),
                (273, 4, strips, 0),
                (274, 3, 1, orientation),
                (277, 3, 1, 4),
                (278, 4, 1, RowsPerStrip),
                (279, 4, strips, 0),
                (338, 3, 1, 2),
            ];
            int depths = 8 + 2 + (12 * entries.Length) + 4;
            int offsets = depths + 8;
            int lengths = offsets + (4 * strips);
            using MemoryStream file = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(file);
            writer.Write([(byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0]);
            writer.Write((ushort)entries.Length);
            foreach ((ushort tag, ushort type, int count, int value) in entries)
            {
                writer.Write(tag);
                writer.Write(type);
                writer.Write(count);
                writer.Write(tag switch { 258 => depths, 273 => offsets, 279 => lengths, _ => value });
            }

            writer.Write(0);
            writer.Write([8, 0, 8, 0, 8, 0, 8, 0]);
            for (int index = 0; index < strips; index++)
            {
                writer.Write(lengths + (4 * strips));
            }

            for (int index = 0; index < strips; index++)
            {
                writer.Write(strip.Length);
            }

            writer.Write(strip);
            return file.ToArray();
        }

        /// <summary><paramref name="png"/> saying it is of another size.</summary>
        public static byte[] PngSaying(byte[] png, int width, int height)
        {
            byte[] changed = [.. png];
            BinaryPrimitives.WriteInt32BigEndian(changed.AsSpan(16), width);
            BinaryPrimitives.WriteInt32BigEndian(changed.AsSpan(20), height);
            BinaryPrimitives.WriteUInt32BigEndian(changed.AsSpan(29), Crc32.HashToUInt32(changed.AsSpan(12, 17)));
            return changed;
        }

        /// <summary>An EXIF profile that says <paramref name="orientation"/> and nothing else.</summary>
        public static byte[] Exif(ushort orientation)
        {
            byte[] exif = [.. Encoding.ASCII.GetBytes("Exif\0\0MM\0*"), 0, 0, 0, 8, 0, 1, 0x01, 0x12, 0, 3, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0];
            BinaryPrimitives.WriteUInt16BigEndian(exif.AsSpan(24), orientation);
            return exif;
        }

        private static byte[] PngChunk(string type, byte[] data)
        {
            byte[] chunk = [0, 0, 0, 0, .. Encoding.ASCII.GetBytes(type), .. data, 0, 0, 0, 0];
            BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
            BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc32.HashToUInt32(chunk.AsSpan(4, 4 + data.Length)));
            return chunk;
        }

        private static byte[] WebP(MagickImage image)
        {
            image.Settings.SetDefine("webp:lossless", "true");
            return image.ToByteArray(MagickFormat.WebP);
        }
    }
}
