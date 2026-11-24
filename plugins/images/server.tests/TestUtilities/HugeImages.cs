using System.Buffers.Binary;
using System.Text;
using ImageMagick;

namespace ByteShelf.Plugins.Images.Tests.TestUtilities
{
    /// <summary>Files of a few hundred bytes that say they are larger than is decoded, as a bomb does, by the width of one pixel.</summary>
    internal static class HugeImages
    {
        /// <summary><paramref name="image"/> in a format, saying it is 12001 by 10000 pixels.</summary>
        public static byte[] Of(string format, MagickImage image)
        {
            switch (format)
            {
                case "png":
                    return TestImages.PngSaying(image.ToByteArray(MagickFormat.Png), 12_001, 10_000);
                case "x-icon":
                    return TestImages.Ico(TestImages.PngSaying(image.ToByteArray(MagickFormat.Png), 12_001, 10_000));
                case "heic":
                    return HeicFile.Make(saidWidth: 12_001, saidHeight: 10_000);
                case "jpeg":
                {
                    byte[] jpeg = TestImages.Jpeg(image);
                    int frame = jpeg.AsSpan().IndexOf(new byte[] { 0xFF, 0xC0 });
                    BinaryPrimitives.WriteUInt16BigEndian(jpeg.AsSpan(frame + 5), 10_000);
                    BinaryPrimitives.WriteUInt16BigEndian(jpeg.AsSpan(frame + 7), 12_001);
                    return jpeg;
                }

                case "bmp":
                {
                    byte[] bmp = image.ToByteArray(MagickFormat.Bmp);
                    BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), 12_001);
                    BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), 10_000);
                    return bmp;
                }

                case "tiff":
                {
                    byte[] tiff = image.ToByteArray(MagickFormat.Tiff);
                    int directory = BinaryPrimitives.ReadInt32LittleEndian(tiff.AsSpan(4));
                    for (int entry = directory + 2; entry < directory + 2 + (12 * tiff[directory]); entry += 12)
                    {
                        // Its width and its height.
                        ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(tiff.AsSpan(entry));
                        if (tag is 256 or 257)
                        {
                            BinaryPrimitives.WriteInt32LittleEndian(tiff.AsSpan(entry + 8), tag == 256 ? 12_001 : 10_000);
                        }
                    }

                    return tiff;
                }

                case "gif":
                case "gif frame":
                {
                    byte[] gif = image.ToByteArray(MagickFormat.Gif);

                    // The canvas, or the frame, whose descriptor begins with a comma, and its offset with zeroes.
                    int offset = format == "gif" ? 6 : gif.AsSpan().IndexOf(new byte[] { (byte)',', 0, 0, 0, 0 }) + 5;
                    BinaryPrimitives.WriteUInt16LittleEndian(gif.AsSpan(offset), 12_001);
                    BinaryPrimitives.WriteUInt16LittleEndian(gif.AsSpan(offset + 2), 10_000);
                    return gif;
                }

                case "webp":
                {
                    // VP8L: 14 bits of width less one and as many of height, after the format's 12 bytes, the chunk's 8 and a signature.
                    byte[] webp = TestImages.Of("webp", image);
                    Assert.Equal("VP8L", Encoding.ASCII.GetString(webp, 12, 4));
                    uint bits = BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(21)) & 0xF0000000;
                    BinaryPrimitives.WriteUInt32LittleEndian(webp.AsSpan(21), bits | 12_000 | (9_999 << 14));
                    return webp;
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(format), format, null);
            }
        }
    }
}
