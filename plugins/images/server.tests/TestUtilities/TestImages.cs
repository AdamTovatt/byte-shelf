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

        /// <summary>The formats that have EXIF's orientation.</summary>
        public static IReadOnlyList<string> FormatsWithOrientation { get; } = ["jpeg", "png", "webp", "tiff"];

        /// <summary>EXIF's orientations, by their values.</summary>
        public static IReadOnlyList<ushort> Orientations { get; } = [1, 2, 3, 4, 5, 6, 7, 8];

        /// <summary>The colours of the quarters of <see cref="Quarters"/> stored as <paramref name="orientation"/> says, upright, in the order of <see cref="QuarterColors"/>.</summary>
        public static IReadOnlyList<MagickColor> UprightQuarterColors(ushort orientation)
        {
            int[][] upright = [[0, 1, 2, 3], [1, 0, 3, 2], [3, 2, 1, 0], [2, 3, 0, 1], [0, 2, 1, 3], [2, 0, 3, 1], [3, 1, 2, 0], [1, 3, 0, 2]];
            return [.. upright[orientation - 1].Select(quarter => QuarterColors[quarter])];
        }

        /// <summary><see cref="Quarters"/>, stored as <paramref name="orientation"/> says.</summary>
        public static MagickImage QuartersStored(int width, int height, ushort orientation)
        {
            MagickImage image = Quarters(width, height);
            image.SetProfile(new ImageProfile("exif", Exif(orientation)));
            image.Orientation = (Orientation)orientation;
            return image;
        }

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

            // Every strip is the one there is: deflated (8), of red, green, blue (2) and what of each is seen, unassociated (2).
            int strips = height / RowsPerStrip;
            return TiffFile.Write(new TiffFile.ImageDirectory(
                [
                    TiffFile.Entry.Of(TiffFile.ImageWidth, TiffFile.Long, (uint)width),
                    TiffFile.Entry.Of(TiffFile.ImageLength, TiffFile.Long, (uint)height),
                    TiffFile.Entry.Of(TiffFile.BitsPerSample, TiffFile.Short, 8, 8, 8, 8),
                    TiffFile.Entry.Of(TiffFile.Compression, TiffFile.Short, 8),
                    TiffFile.Entry.Of(TiffFile.PhotometricInterpretation, TiffFile.Short, 2),
                    TiffFile.Entry.StripsAt(strips),
                    TiffFile.Entry.Of(TiffFile.Orientation, TiffFile.Short, orientation),
                    TiffFile.Entry.Of(TiffFile.SamplesPerPixel, TiffFile.Short, 4),
                    TiffFile.Entry.Of(TiffFile.RowsPerStrip, TiffFile.Long, RowsPerStrip),
                    TiffFile.Entry.StripLengths(strips),
                    TiffFile.Entry.Of(TiffFile.ExtraSamples, TiffFile.Short, 2),
                ],
                compressed.ToArray(),
                []));
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

        /// <summary>An EXIF profile that says the orientation is upright and has a GPS part, whose map datum is <paramref name="mapDatum"/>.</summary>
        public static byte[] ExifWithGps(string mapDatum)
        {
            // Offsets are from the TIFF header, after "Exif\0\0": the first IFD at 8 with two entries, then the GPS IFD with one, then the datum.
            const int GpsIfd = 8 + 2 + (2 * 12) + 4;
            const int Datum = GpsIfd + 2 + 12 + 4;
            byte[] datum = [.. Encoding.ASCII.GetBytes(mapDatum), 0];
            using MemoryStream tiff = new MemoryStream();
            void Write16(int value) => tiff.Write([(byte)(value >> 8), (byte)value]);
            void Write32(int value) => tiff.Write([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
            tiff.Write("MM\0*"u8);
            Write32(8);
            Write16(2);
            Write16(0x0112);
            Write16(3);
            Write32(1);
            Write16(1);
            Write16(0);
            Write16(0x8825);
            Write16(4);
            Write32(1);
            Write32(GpsIfd);
            Write32(0);
            Write16(1);
            Write16(0x0012);
            Write16(2);
            Write32(datum.Length);
            Write32(Datum);
            Write32(0);
            tiff.Write(datum);
            return [.. Encoding.ASCII.GetBytes("Exif\0\0"), .. tiff.ToArray()];
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
