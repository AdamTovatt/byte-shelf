using System.Buffers.Binary;
using System.Text;

namespace ByteShelf.Plugins.Images
{
    /// <summary>An ICC profile (version 4.0) of Display P3: its three primaries and white, relative to ICC's D50, and sRGB's curve.</summary>
    internal static class DisplayP3Profile
    {
        private const int HeaderBytes = 128;

        /// <summary>The bytes of the profile.</summary>
        public static byte[] Bytes { get; } = Make();

        private static byte[] Make()
        {
            byte[] curve = Curve(2.4, 1 / 1.055, 0.055 / 1.055, 1 / 12.92, 0.04045);
            (string Signature, byte[] Data)[] tags =
            [
                ("desc", Text("Display P3")),
                ("cprt", Text("No copyright, use freely")),
                ("wtpt", Xyz(0.9642, 1, 0.8249)),
                ("rXYZ", Xyz(0.51512, 0.2412, -0.00105)),
                ("gXYZ", Xyz(0.29198, 0.69225, 0.04189)),
                ("bXYZ", Xyz(0.1571, 0.06657, 0.78407)),
                ("rTRC", curve),
                ("gTRC", curve),
                ("bTRC", curve),
            ];

            int offset = HeaderBytes + 4 + (12 * tags.Length);
            byte[] profile = new byte[offset + tags.Sum(tag => tag.Data.Length)];
            Write(profile, 0, profile.Length);
            Write(profile, 8, 0x04000000);
            Write(profile, 12, "mntrRGB XYZ ");
            Write(profile, 24, 0x07E80001);
            Write(profile, 28, 0x00010000);
            Write(profile, 36, "acsp");
            Xyz(0.9642, 1, 0.8249).AsSpan(8).CopyTo(profile.AsSpan(68));
            Write(profile, HeaderBytes, tags.Length);
            for (int index = 0; index < tags.Length; index++)
            {
                int entry = HeaderBytes + 4 + (12 * index);
                Write(profile, entry, tags[index].Signature);
                Write(profile, entry + 4, offset);
                Write(profile, entry + 8, tags[index].Data.Length);
                tags[index].Data.CopyTo(profile, offset);
                offset += tags[index].Data.Length;
            }

            return profile;
        }

        private static byte[] Xyz(double x, double y, double z)
        {
            byte[] element = new byte[8 + 12];
            Write(element, 0, "XYZ ");
            WriteFixed(element, 8, x, y, z);
            return element;
        }

        /// <summary>A parametric curve: below <paramref name="d"/> it is <paramref name="c"/> x, and (<paramref name="a"/> x + <paramref name="b"/>) to the <paramref name="g"/> from there.</summary>
        private static byte[] Curve(double g, double a, double b, double c, double d)
        {
            // The function is the one of these five parameters, the type's third from zero.
            byte[] element = new byte[12 + 20];
            Write(element, 0, "para");
            Write(element, 8, 3 << 16);
            WriteFixed(element, 12, g, a, b, c, d);
            return element;
        }

        /// <summary>The text, as the one English text of a tag.</summary>
        private static byte[] Text(string text)
        {
            byte[] characters = Encoding.BigEndianUnicode.GetBytes(text);
            byte[] element = new byte[(28 + characters.Length + 3) / 4 * 4];
            Write(element, 0, "mluc");
            Write(element, 8, 1);
            Write(element, 12, 12);
            Write(element, 16, "enUS");
            Write(element, 20, characters.Length);
            Write(element, 24, 28);
            characters.CopyTo(element, 28);
            return element;
        }

        private static void WriteFixed(byte[] bytes, int offset, params double[] values)
        {
            for (int index = 0; index < values.Length; index++)
            {
                Write(bytes, offset + (4 * index), (int)Math.Round(values[index] * 65536));
            }
        }

        private static void Write(byte[] bytes, int offset, int value)
        {
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset), value);
        }

        private static void Write(byte[] bytes, int offset, string signature)
        {
            Encoding.ASCII.GetBytes(signature).CopyTo(bytes, offset);
        }
    }
}
