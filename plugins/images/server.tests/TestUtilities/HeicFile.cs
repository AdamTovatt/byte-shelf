using System.Buffers.Binary;
using System.Text;

namespace ByteShelf.Plugins.Images.Tests.TestUtilities
{
    /// <summary>
    /// A HEIC file of 64 by 48 pixels, its left half red and its right half blue, which ImageMagick cannot write. Its one
    /// picture was encoded for these tests (ffmpeg's libx265, from two plain colours); the boxes around it are made here,
    /// as ISO/IEC 23008-12 has them.
    /// </summary>
    internal static class HeicFile
    {
        public const int StoredWidth = 64;

        public const int StoredHeight = 48;

        /// <summary>The colours as marked by ISO/IEC 23091-2: sRGB's.</summary>
        public const ushort SrgbPrimaries = 1;

        public const ushort DisplayP3Primaries = 12;

        public const ushort SrgbTransfer = 13;

        public const ushort PqTransfer = 16;

        /// <summary>The decoder's configuration (<c>hvcC</c>).</summary>
        private static readonly byte[] Configuration = Convert.FromBase64String(
            "AQQIAAAAn6gAAAAAHvAA/P34+AAADwOgAAEAF0ABDAH//wQIAAADAJ+oAAADAAAeugJAoQABACpCAQEECAAAAwCfqAAAAwAAHqAggxZbq5MrwFuAhoMCAAADAAIAAAMAAhCiAAEABkQBwXPAiQ==");

        private static readonly byte[] Picture = Convert.FromBase64String(
            "AAAAF0ABDAH//wQIAAADAJ+oAAADAAAeugJAAAAAKkIBAQQIAAADAJ+oAAADAAAeoCCDFlurkyvAW4CGgwIAAAMAAgAAAwACEAAAAAZEAcFzwIkAAABeKAGvBPhxZKZv/RqS///LG/f/4+KeFIP8+Fx9/qTIf/9k1tn24wFXSn4qLSc7I5o71eUTirnT2zrqAA2jzFHTBNiASsXvd3bKuJd4dVO3IUyS5hQlecH/JNzWyLXgYA==");

        /// <param name="quarterTurns">How many quarter turns counterclockwise it is shown at.</param>
        /// <param name="primaries">What its colours are of.</param>
        /// <param name="transfer">What their numbers are of their light.</param>
        /// <param name="icc">An ICC profile, instead.</param>
        /// <param name="exif">An EXIF profile, with the header it has in a JPEG.</param>
        /// <param name="saidWidth">The size it says it is of, when that is another.</param>
        public static byte[] Make(int quarterTurns = 0, ushort primaries = SrgbPrimaries, ushort transfer = SrgbTransfer, byte[]? icc = null, byte[]? exif = null, int saidWidth = StoredWidth, int saidHeight = StoredHeight)
        {
            byte[] colors = icc == null
                ? Box("colr", Ascii("nclx"), Short(primaries), Short(transfer), Short(6), [0x80])
                : Box("colr", Ascii("prof"), icc);
            byte[] properties = Box(
                "iprp",
                Box("ipco", Box("hvcC", Configuration), FullBox("ispe", Int(saidWidth), Int(saidHeight)), colors, Box("irot", [(byte)quarterTurns])),
                FullBox("ipma", Int(1), Short(1), [4], [0x81, 0x02, 0x03, 0x84]));

            // The bytes the EXIF item begins with say where in it the TIFF header is: after "Exif\0\0".
            byte[] exifItem = exif == null ? [] : [.. Int(6), .. exif];
            byte[][] entries = exif == null ? [Entry(1, "hvc1")] : [Entry(1, "hvc1"), Entry(2, "Exif")];
            byte[] references = exif == null ? [] : FullBox("iref", Box("cdsc", Short(2), Short(1), Short(1)));

            byte[] MetaAt(int mediaOffset)
            {
                byte[] locations = exif == null
                    ? FullBox("iloc", [0x44, 0x00], Short(1), Location(1, mediaOffset, Picture.Length))
                    : FullBox("iloc", [0x44, 0x00], Short(2), Location(1, mediaOffset, Picture.Length), Location(2, mediaOffset + Picture.Length, exifItem.Length));
                return FullBox(
                    "meta",
                    FullBox("hdlr", Int(0), Ascii("pict"), new byte[13]),
                    FullBox("pitm", Short(1)),
                    locations,
                    FullBox("iinf", [Short(entries.Length), .. entries]),
                    references,
                    properties);
            }

            byte[] type = Box("ftyp", Ascii("heic"), Int(0), Ascii("mif1"), Ascii("heic"));
            byte[] meta = MetaAt(0);
            return [.. type, .. MetaAt(type.Length + meta.Length + 8), .. Box("mdat", Picture, exifItem)];
        }

        private static byte[] Entry(int id, string type)
        {
            byte[] entry = FullBox("infe", Short(id), Short(0), Ascii(type), [0]);
            entry[8] = 2;
            return entry;
        }

        private static byte[] Location(int id, int offset, int length)
        {
            return [.. Short(id), .. Short(0), .. Short(1), .. Int(offset), .. Int(length)];
        }

        private static byte[] FullBox(string type, params byte[][] parts)
        {
            return Box(type, [Int(0), .. parts]);
        }

        private static byte[] Box(string type, params byte[][] parts)
        {
            byte[] content = [.. parts.SelectMany(part => part)];
            return [.. Int(content.Length + 8), .. Ascii(type), .. content];
        }

        private static byte[] Ascii(string text)
        {
            return Encoding.ASCII.GetBytes(text);
        }

        private static byte[] Short(int value)
        {
            byte[] bytes = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
            return bytes;
        }

        private static byte[] Int(int value)
        {
            byte[] bytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            return bytes;
        }
    }
}
