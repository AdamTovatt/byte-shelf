using System.Buffers.Binary;
using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Writer;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>
    /// Photos, videos and PDFs carrying metadata, written by hand byte by byte where no library writes
    /// it: EXIF segments in a JPEG and QuickTime boxes.
    /// </summary>
    internal static class TestMedia
    {
        private const ushort Ascii = 2;
        private const ushort Long = 4;
        private const ushort Rational = 5;

        private static readonly DateTime QuickTimeEpoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// A small JPEG whose EXIF holds whichever of these are given: when it was taken as the camera's
        /// clock shows it (<paramref name="taken"/>, <c>yyyy:MM:dd HH:mm:ss</c>), the offset it recorded
        /// for that (such as <c>+02:00</c>), a GPS position, the camera's make and model, and when it was
        /// digitized (<paramref name="digitized"/>, as <paramref name="taken"/>, without an offset).
        /// </summary>
        public static byte[] Photo(string? taken = null, string? offset = null, (double Latitude, double Longitude)? position = null, string? make = null, string? model = null, string? digitized = null)
        {
            List<TiffField> ifd0 = new List<TiffField>();
            AddText(ifd0, 0x010F, make);
            AddText(ifd0, 0x0110, model);

            List<TiffField> exif = new List<TiffField>();
            AddText(exif, 0x9003, taken);
            AddText(exif, 0x9004, digitized);
            AddText(exif, 0x9011, offset);

            List<TiffField> gps = new List<TiffField>();
            if (position is (double latitude, double longitude))
            {
                AddText(gps, 0x0001, latitude < 0 ? "S" : "N");
                gps.Add(new TiffField(0x0002, Rational, 3, Degrees(Math.Abs(latitude))));
                AddText(gps, 0x0003, longitude < 0 ? "W" : "E");
                gps.Add(new TiffField(0x0004, Rational, 3, Degrees(Math.Abs(longitude))));
            }

            // The pointers are four bytes whatever they point at, so the sizes are known before the offsets.
            int exifOffset = 8 + SizeOf(ifd0) + (12 * 2);
            int gpsOffset = exifOffset + SizeOf(exif);
            ifd0.Add(new TiffField(0x8769, Long, 1, LittleEndian((uint)exifOffset)));
            ifd0.Add(new TiffField(0x8825, Long, 1, LittleEndian((uint)gpsOffset)));

            byte[] tiff = new byte[gpsOffset + SizeOf(gps)];
            "II*\0"u8.CopyTo(tiff);
            BinaryPrimitives.WriteInt32LittleEndian(tiff.AsSpan(4), 8);
            WriteIfd(tiff, 8, ifd0);
            WriteIfd(tiff, exifOffset, exif);
            WriteIfd(tiff, gpsOffset, gps);

            byte[] jpeg = SmallJpeg();
            byte[] payload = [.. "Exif\0\0"u8, .. tiff];
            byte[] segment = new byte[4 + payload.Length];
            segment[0] = 0xFF;
            segment[1] = 0xE1;
            BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(2 + payload.Length));
            payload.CopyTo(segment, 4);
            return [.. jpeg.AsSpan(0, 2), .. segment, .. jpeg.AsSpan(2)];
        }

        /// <summary>
        /// A QuickTime movie of only its header boxes: a movie header saying it was made at
        /// <paramref name="created"/> (null for zero, which cameras write when they do not know) and lasts
        /// <paramref name="duration"/>, and Apple's or Android's metadata keys for whatever else is given.
        /// </summary>
        public static byte[] Movie(
            DateTimeOffset? created,
            TimeSpan duration,
            string? location = null,
            string? creationDate = null,
            string? make = null,
            string? model = null,
            string? androidManufacturer = null,
            string? androidModel = null)
        {
            const uint TimeScale = 1000;
            uint createdSeconds = created == null ? 0 : (uint)(created.Value.UtcDateTime - QuickTimeEpoch).TotalSeconds;
            byte[] movieHeader = Box(
                "mvhd",
                BigEndian(0),
                BigEndian(createdSeconds),
                BigEndian(createdSeconds),
                BigEndian(TimeScale),
                BigEndian((uint)(duration.TotalSeconds * TimeScale)),
                BigEndian(0x00010000),
                [1, 0],
                new byte[10],
                new byte[36],
                new byte[24],
                BigEndian(2));

            List<(string Key, string Value)> items = new List<(string, string)>();
            AddItem(items, "com.apple.quicktime.location.ISO6709", location);
            AddItem(items, "com.apple.quicktime.creationdate", creationDate);
            AddItem(items, "com.apple.quicktime.make", make);
            AddItem(items, "com.apple.quicktime.model", model);
            AddItem(items, "com.android.manufacturer", androidManufacturer);
            AddItem(items, "com.android.model", androidModel);

            byte[] movie = items.Count == 0 ? Box("moov", movieHeader) : Box("moov", movieHeader, MetadataBox(items));
            return [.. Box("ftyp", "qt  "u8.ToArray(), BigEndian(0), "qt  "u8.ToArray()), .. movie];
        }

        /// <summary>A PDF of <paramref name="pages"/> blank pages, titled <paramref name="title"/> unless null.</summary>
        public static byte[] Pdf(int pages, string? title)
        {
            PdfDocumentBuilder builder = new PdfDocumentBuilder();
            for (int page = 0; page < pages; page++)
            {
                builder.AddPage(PageSize.A4);
            }

            if (title != null)
            {
                builder.DocumentInformation.Title = title;
            }

            return builder.Build();
        }

        /// <summary>An 8 by 8 pixel JPEG of one colour.</summary>
        private static byte[] SmallJpeg()
        {
            return Convert.FromBase64String(
                "/9j/4AAQSkZJRgABAgAAAQABAAD/2wBDAAgEBAQEBAUFBQUFBQYGBgYGBgYGBgYGBgYHBwcICAgHBwcGBgcHCAgICAkJCQgICAgJCQoKCgwMCwsODg4RERT/" +
                "xABLAAEBAAAAAAAAAAAAAAAAAAAABAEBAAAAAAAAAAAAAAAAAAAABhABAAAAAAAAAAAAAAAAAAAAABEBAAAAAAAAAAAAAAAAAAAAAP/AABEIAAgACAMBIgAC" +
                "EQADEQD/2gAMAwEAAhEDEQA/AJQCMdf/2Q==");
        }

        private static void AddText(List<TiffField> fields, ushort tag, string? text)
        {
            if (text != null)
            {
                byte[] value = [.. Encoding.ASCII.GetBytes(text), 0];
                fields.Add(new TiffField(tag, Ascii, (uint)value.Length, value));
            }
        }

        /// <summary>Degrees as EXIF writes them: whole degrees, whole minutes and seconds to the thousandth, each a rational.</summary>
        private static byte[] Degrees(double value)
        {
            double degrees = Math.Floor(value);
            double minutes = Math.Floor((value - degrees) * 60);
            double seconds = (((value - degrees) * 60) - minutes) * 60;
            uint[] parts = [(uint)degrees, 1, (uint)minutes, 1, (uint)Math.Round(seconds * 1000), 1000];
            return parts.SelectMany(LittleEndian).ToArray();
        }

        /// <summary>An IFD's entries, its link to the next and the values too long to sit in an entry, each padded to an even length.</summary>
        private static int SizeOf(List<TiffField> ifd)
        {
            return 2 + (12 * ifd.Count) + 4 + ifd.Where(field => field.Value.Length > 4).Sum(field => Even(field.Value.Length));
        }

        private static void WriteIfd(byte[] tiff, int offset, List<TiffField> ifd)
        {
            List<TiffField> sorted = ifd.OrderBy(field => field.Tag).ToList();
            BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(offset), (ushort)sorted.Count);
            int data = offset + 2 + (12 * sorted.Count) + 4;
            for (int index = 0; index < sorted.Count; index++)
            {
                TiffField field = sorted[index];
                Span<byte> entry = tiff.AsSpan(offset + 2 + (12 * index), 12);
                BinaryPrimitives.WriteUInt16LittleEndian(entry, field.Tag);
                BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], field.Type);
                BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], field.Count);
                if (field.Value.Length <= 4)
                {
                    field.Value.CopyTo(entry[8..]);
                }
                else
                {
                    BinaryPrimitives.WriteInt32LittleEndian(entry[8..], data);
                    field.Value.CopyTo(tiff, data);
                    data += Even(field.Value.Length);
                }
            }
        }

        private static int Even(int length)
        {
            return length + (length % 2);
        }

        private static byte[] LittleEndian(uint value)
        {
            byte[] bytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            return bytes;
        }

        private static void AddItem(List<(string Key, string Value)> items, string key, string? value)
        {
            if (value != null)
            {
                items.Add((key, value));
            }
        }

        /// <summary>Apple's <c>meta</c> box: a handler, the keys, and an item list whose items are named by their key's index from 1.</summary>
        private static byte[] MetadataBox(List<(string Key, string Value)> items)
        {
            byte[] handler = Box("hdlr", BigEndian(0), BigEndian(0), "mdta"u8.ToArray(), new byte[12], new byte[1]);
            byte[] keys = Box(
                "keys",
                BigEndian(0),
                BigEndian((uint)items.Count),
                items.SelectMany(item =>
                {
                    byte[] key = Encoding.UTF8.GetBytes(item.Key);
                    return BigEndian((uint)(8 + key.Length)).Concat("mdta"u8.ToArray()).Concat(key);
                }).ToArray());

            byte[] list = Box("ilst", items.SelectMany((item, index) =>
            {
                // UTF-8 text is data type 1.
                byte[] data = Box("data", BigEndian(1), BigEndian(0), Encoding.UTF8.GetBytes(item.Value));
                return BigEndian((uint)(8 + data.Length)).Concat(BigEndian((uint)(index + 1))).Concat(data);
            }).ToArray());

            return Box("meta", handler, keys, list);
        }

        private static byte[] Box(string type, params byte[][] parts)
        {
            byte[] body = parts.SelectMany(part => part).ToArray();
            byte[] box = new byte[8 + body.Length];
            BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
            Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
            body.CopyTo(box, 8);
            return box;
        }

        private static byte[] BigEndian(uint value)
        {
            byte[] bytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
            return bytes;
        }

        private sealed record TiffField(ushort Tag, ushort Type, uint Count, byte[] Value);
    }
}
