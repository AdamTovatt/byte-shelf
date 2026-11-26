using System.Buffers.Binary;
using ImageMagick;

namespace ByteShelf.Plugins.Images
{
    /// <summary>
    /// A camera's raw, as LibRaw (ImageMagick's DNG) reads one: which TIFFs may be one, whether LibRaw reads them, the JPEG
    /// the camera made of one, and how one is decoded.
    /// </summary>
    internal static class CameraRaw
    {
        /// <summary>What LibRaw answers of contents it does not read as a camera's raw.</summary>
        private const string NotRaw = "not RAW file";

        /// <summary>Where ImageMagick puts what the camera made of the raw to show, when asked to: a JPEG, or a bitmap's pixels in no format.</summary>
        private const string CamerasCopy = "dng:thumbnail";

        private const int EntryBytes = 12;
        private const ushort ShortType = 3;

        private const ushort NewSubfileTypeTag = 254;
        private const ushort PhotometricTag = 262;
        private const ushort DngVersionTag = 50706;

        /// <summary>The bit of a new subfile type that says it is a smaller copy of another.</summary>
        private const uint ReducedImage = 1;

        /// <summary>The photometric interpretations of a sensor's pixels: a colour filter array's, and DNG's linear raw.</summary>
        private const uint ColorFilterArray = 32803;
        private const uint LinearRaw = 34892;

        /// <summary>
        /// Whether the TIFF that <paramref name="contents"/> are, whose first bytes <paramref name="head"/> is, says it may be
        /// a camera's raw: by a CR2's mark, or in its first directory by being a smaller copy, as an ARW's or a NEF's is, by
        /// a DNG's version, or by pixels of a sensor's. Only those are asked of LibRaw, which takes others for raws too, as
        /// a TIFF of 16-bit grays that names a camera, and brightens them.
        /// </summary>
        public static async Task<bool> MayBeAsync(Stream contents, ReadOnlyMemory<byte> head)
        {
            if (head.Span is [(byte)'I', (byte)'I', 42, 0, _, _, _, _, (byte)'C', (byte)'R', ..])
            {
                return true;
            }

            bool isLittleEndian = head.Span[0] == (byte)'I';

            // A BigTIFF is no camera's.
            if (head.Length < 8 || head.Span[isLittleEndian ? 2 : 3] != 42)
            {
                return false;
            }

            contents.Position = UInt32(head.Span[4..], isLittleEndian);
            byte[] count = new byte[2];
            if (await contents.ReadAtLeastAsync(count, count.Length, throwOnEndOfStream: false) < count.Length)
            {
                return false;
            }

            byte[] entries = new byte[UInt16(count, isLittleEndian) * EntryBytes];
            int read = await contents.ReadAtLeastAsync(entries, entries.Length, throwOnEndOfStream: false);
            for (int offset = 0; offset + EntryBytes <= read; offset += EntryBytes)
            {
                ReadOnlySpan<byte> entry = entries.AsSpan(offset, EntryBytes);
                ushort tag = UInt16(entry, isLittleEndian);
                uint value = UInt16(entry[2..], isLittleEndian) == ShortType ? UInt16(entry[8..], isLittleEndian) : UInt32(entry[8..], isLittleEndian);
                if ((tag == NewSubfileTypeTag && (value & ReducedImage) != 0)
                    || (tag == PhotometricTag && value is ColorFilterArray or LinearRaw)
                    || tag == DngVersionTag)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether LibRaw reads <paramref name="contents"/> as a camera's raw, which it has then pinged into
        /// <paramref name="images"/> by <paramref name="settings"/>, with what the camera made of it to show.
        /// </summary>
        /// <exception cref="MagickException">They are damaged.</exception>
        public static bool Ping(Codecs codecs, MagickImageCollection images, Stream contents, MagickReadSettings settings)
        {
            settings.SetDefine("dng:read-thumbnail", "true");
            try
            {
                codecs.Ping(images, contents, settings);
                return true;
            }
            catch (MagickCoderErrorException exception) when (exception.Message.Contains(NotRaw, StringComparison.Ordinal))
            {
                return false;
            }
        }

        /// <summary>
        /// The JPEG the camera made of the <paramref name="raw"/> pinged, as it is oriented by its own EXIF, or by the raw's
        /// that LibRaw gives one without; null when there is none, or a bitmap.
        /// </summary>
        public static byte[]? JpegOf(IMagickImage<byte> raw)
        {
            return raw.GetProfile(CamerasCopy)?.ToByteArray() is { } copy && ImageFormat.Of(copy) == ImageFormat.Jpeg ? copy : null;
        }

        /// <summary>
        /// Has LibRaw decode one white balanced as shot, as its camera's JPEG is: ImageMagick's default too, said here so
        /// that another default changes no colours.
        /// </summary>
        public static void SetDecoding(MagickReadSettings settings)
        {
            settings.SetDefine("dng:use-camera-wb", "true");
        }

        private static ushort UInt16(ReadOnlySpan<byte> bytes, bool isLittleEndian)
        {
            return isLittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes);
        }

        private static uint UInt32(ReadOnlySpan<byte> bytes, bool isLittleEndian)
        {
            return isLittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes);
        }
    }
}
