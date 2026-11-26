using System.Buffers.Binary;
using System.Text;

namespace ByteShelf.Plugins.Images.Tests.TestUtilities
{
    /// <summary>
    /// TIFFs made here, little-endian, as the TIFF 6.0 specification has them: the header, then each image's directory with
    /// the values that do not fit in its entries, then the strips of the smaller copies before the first image's, as a
    /// camera lays out its raw.
    /// </summary>
    internal static class TiffFile
    {
        public const ushort NewSubfileType = 254;
        public const ushort ImageWidth = 256;
        public const ushort ImageLength = 257;
        public const ushort BitsPerSample = 258;
        public const ushort Compression = 259;
        public const ushort PhotometricInterpretation = 262;
        public const ushort Make = 271;
        public const ushort Model = 272;
        public const ushort StripOffsets = 273;
        public const ushort Orientation = 274;
        public const ushort SamplesPerPixel = 277;
        public const ushort RowsPerStrip = 278;
        public const ushort StripByteCounts = 279;
        public const ushort PlanarConfiguration = 284;
        public const ushort SubIfds = 330;
        public const ushort ExtraSamples = 338;

        public const ushort Byte = 1;
        public const ushort Ascii = 2;
        public const ushort Short = 3;
        public const ushort Long = 4;
        public const ushort Rational = 5;
        public const ushort SignedRational = 10;

        /// <summary>What the fractions it writes are of.</summary>
        private const int Denominator = 10_000;

        private const int EntryBytes = 12;

        /// <summary>The file of <paramref name="first"/>, the first image, and the smaller copies it says it has.</summary>
        public static byte[] Write(ImageDirectory first)
        {
            List<ImageDirectory> directories = [first, .. first.SmallerCopies];
            Dictionary<ImageDirectory, int> directoryAt = [];
            Dictionary<ImageDirectory, int> stripAt = [];
            int offset = 8;
            foreach (ImageDirectory directory in directories)
            {
                directoryAt[directory] = offset;
                offset += directory.Length;
            }

            foreach (ImageDirectory directory in first.SmallerCopies.Append(first))
            {
                stripAt[directory] = offset;
                offset += Even(directory.Strip.Length);
            }

            byte[] file = new byte[offset];
            Encoding.ASCII.GetBytes("II*\0").CopyTo(file, 0);
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), 8);
            foreach (ImageDirectory directory in directories)
            {
                int entryAt = directoryAt[directory];
                int valueAt = entryAt + 2 + (EntryBytes * directory.Entries.Count) + 4;
                BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(entryAt), (ushort)directory.Entries.Count);
                entryAt += 2;
                foreach (Entry entry in directory.Entries)
                {
                    byte[] value = entry.Value
                        ?? (entry.Tag switch
                        {
                            StripOffsets => Repeated(stripAt[directory], entry.Count),
                            StripByteCounts => Repeated(directory.Strip.Length, entry.Count),
                            SubIfds => [.. directory.SmallerCopies.SelectMany(copy => BitConverter.GetBytes(directoryAt[copy]))],
                            _ => throw new InvalidOperationException($"The tag {entry.Tag} has no value."),
                        });
                    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(entryAt), entry.Tag);
                    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(entryAt + 2), entry.Type);
                    BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(entryAt + 4), entry.Count);
                    if (value.Length <= 4)
                    {
                        value.CopyTo(file, entryAt + 8);
                    }
                    else
                    {
                        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(entryAt + 8), valueAt);
                        value.CopyTo(file, valueAt);
                        valueAt += Even(value.Length);
                    }

                    entryAt += EntryBytes;
                }

                directory.Strip.CopyTo(file, stripAt[directory]);
            }

            return file;
        }

        /// <summary>Where each thing in the file begins, which TIFF has at an even offset.</summary>
        private static int Even(int length)
        {
            return (length + 1) & ~1;
        }

        private static byte[] Repeated(int value, int count)
        {
            return [.. Enumerable.Repeat(value, count).SelectMany(each => BitConverter.GetBytes(each))];
        }

        /// <summary>An image's directory: its entries, in the order of their tags, its one strip, and the directories of its smaller copies.</summary>
        public sealed record ImageDirectory(IReadOnlyList<Entry> Entries, byte[] Strip, IReadOnlyList<ImageDirectory> SmallerCopies)
        {
            /// <summary>Its bytes: the count of its entries, each entry, the offset of a next directory, which there is not, and the values that do not fit in their entries.</summary>
            public int Length => 2 + (EntryBytes * Entries.Count) + 4 + Entries.Sum(entry => entry.Length > 4 ? Even(entry.Length) : 0);
        }

        /// <summary>A directory's entry: its tag, the type of its values, how many there are, and their bytes; null for where something is in the file, known as it is written.</summary>
        public sealed record Entry(ushort Tag, ushort Type, int Count, byte[]? Value)
        {
            public int Length => Count * (Type switch { Byte or Ascii => 1, Short => 2, Long => 4, _ => 8 });

            /// <summary>Where the strip is, once for each of <paramref name="strips"/>: every one is the strip there is.</summary>
            public static Entry StripsAt(int strips = 1)
            {
                return new Entry(StripOffsets, Long, strips, null);
            }

            /// <summary>How long the strip is, once for each of <paramref name="strips"/>.</summary>
            public static Entry StripLengths(int strips = 1)
            {
                return new Entry(StripByteCounts, Long, strips, null);
            }

            public static Entry SmallerCopiesAt(int count)
            {
                return new Entry(SubIfds, Long, count, null);
            }

            public static Entry Of(ushort tag, ushort type, params uint[] values)
            {
                byte[] bytes = type switch
                {
                    Byte => [.. values.Select(value => (byte)value)],
                    Short => [.. values.SelectMany(value => BitConverter.GetBytes((ushort)value))],
                    _ => [.. values.SelectMany(value => BitConverter.GetBytes(value))],
                };
                return new Entry(tag, type, values.Length, bytes);
            }

            public static Entry Of(ushort tag, string text)
            {
                byte[] bytes = [.. Encoding.ASCII.GetBytes(text), 0];
                return new Entry(tag, Ascii, bytes.Length, bytes);
            }

            /// <summary>Fractions of <see cref="Rational"/> or <see cref="SignedRational"/>.</summary>
            public static Entry OfFractions(ushort tag, ushort type, params double[] values)
            {
                byte[] bytes = [.. values.SelectMany(value => (byte[])[.. BitConverter.GetBytes((int)Math.Round(value * Denominator)), .. BitConverter.GetBytes(Denominator)])];
                return new Entry(tag, type, values.Length, bytes);
            }
        }
    }
}
