using System.Buffers.Binary;
using System.Text;

namespace ByteShelf.Plugins.Images.Tests
{
    /// <summary>Which TIFFs are asked of LibRaw whether they are a camera's raw: those that say they may be, in both of a TIFF's byte orders.</summary>
    public sealed class CameraRawTests
    {
        private const ushort Byte = 1;
        private const ushort Ascii = 2;
        private const ushort Short = 3;
        private const ushort Long = 4;

        [Theory]
        [InlineData(false, 254, Long, 1u, true)]
        [InlineData(true, 254, Long, 1u, true)]
        [InlineData(false, 254, Long, 0u, false)]
        [InlineData(false, 262, Short, 32803u, true)]
        [InlineData(true, 262, Short, 34892u, true)]
        [InlineData(false, 262, Short, 1u, false)]
        [InlineData(true, 262, Short, 2u, false)]
        [InlineData(false, 50706, Byte, 0x01040000u, true)]
        [InlineData(false, 271, Ascii, 0x43616E00u, false)]
        public async Task MayBeAsync_AsksOfThoseWhoseFirstDirectorySaysSo(bool isBigEndian, ushort tag, ushort type, uint value, bool expected)
        {
            byte[] contents = OneEntry(isBigEndian, tag, type, value);

            Assert.Equal(expected, await MayBeAsync(contents));
        }

        [Fact]
        public async Task MayBeAsync_AsksOfACr2ByItsMark()
        {
            Assert.True(await MayBeAsync(Encoding.Latin1.GetBytes("II*\0\u0010\0\0\0CR\u0002\0")));
        }

        [Fact]
        public async Task MayBeAsync_AsksNothingOfABigTiff()
        {
            byte[] contents = OneEntry(isBigEndian: false, 254, Long, 1);
            contents[2] = 43;

            Assert.False(await MayBeAsync(contents));
        }

        [Fact]
        public async Task MayBeAsync_AsksNothingOfOneWhoseFirstDirectoryIsNotThere()
        {
            byte[] contents = OneEntry(isBigEndian: false, 254, Long, 1);
            BinaryPrimitives.WriteInt32LittleEndian(contents.AsSpan(4), contents.Length);

            Assert.False(await MayBeAsync(contents));
        }

        private static async Task<bool> MayBeAsync(byte[] contents)
        {
            using MemoryStream stream = new MemoryStream(contents, writable: false);
            return await CameraRaw.MayBeAsync(stream, contents);
        }

        /// <summary>A TIFF's header and its first directory, of one entry: a short's value, a long's, or four bytes, as <paramref name="value"/> is written big-endian.</summary>
        private static byte[] OneEntry(bool isBigEndian, ushort tag, ushort type, uint value)
        {
            byte[] contents = new byte[8 + 2 + 12 + 4];
            Encoding.Latin1.GetBytes(isBigEndian ? "MM\0*" : "II*\0").CopyTo(contents, 0);
            Write32(contents.AsSpan(4), 8);
            Write16(contents.AsSpan(8), 1);
            Write16(contents.AsSpan(10), tag);
            Write16(contents.AsSpan(12), type);
            Write32(contents.AsSpan(14), type == Short ? 1u : 4u);
            if (type == Short)
            {
                Write16(contents.AsSpan(18), (ushort)value);
            }
            else if (type == Long)
            {
                Write32(contents.AsSpan(18), value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(contents.AsSpan(18), value);
            }

            return contents;

            void Write16(Span<byte> bytes, ushort number)
            {
                if (isBigEndian)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(bytes, number);
                }
                else
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes, number);
                }
            }

            void Write32(Span<byte> bytes, uint number)
            {
                if (isBigEndian)
                {
                    BinaryPrimitives.WriteUInt32BigEndian(bytes, number);
                }
                else
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes, number);
                }
            }
        }
    }
}
