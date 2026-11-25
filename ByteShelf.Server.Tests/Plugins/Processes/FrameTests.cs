using System.Buffers.Binary;
using ByteShelf.Server.Plugins.Processes;

namespace ByteShelf.Server.Tests.Plugins.Processes
{
    /// <summary>What the server and a plugin's process take of each other as frames, and what they refuse.</summary>
    public sealed class FrameTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(Frame.MaxPayloadBytes)]
        public async Task Read_OfWhatWasWritten_IsTheSameFrame(int length)
        {
            byte[] payload = Enumerable.Range(0, length).Select(index => (byte)index).ToArray();
            using MemoryStream stream = new MemoryStream();
            Frame.Write(stream, FrameType.Answer, payload);
            stream.Position = 0;

            Frame? read = await Frame.ReadAsync(stream, CancellationToken.None);

            Assert.Equal(FrameType.Answer, read!.Type);
            Assert.Equal(payload, read.Payload);
        }

        [Fact]
        public void Write_OfAPayloadOneByteOverTheMost_IsRefused()
        {
            using MemoryStream stream = new MemoryStream();

            Assert.Throws<ArgumentOutOfRangeException>(() => Frame.Write(stream, FrameType.Answer, new byte[Frame.MaxPayloadBytes + 1]));
            Assert.Equal(0, stream.Length);
        }

        [Theory]
        [InlineData(Frame.MaxPayloadBytes + 1)]
        [InlineData(-1)]
        public async Task Read_OfAHeaderSayingALengthNoFrameHas_IsNoFrame(int length)
        {
            using MemoryStream stream = new MemoryStream(Header(length));

            await Assert.ThrowsAsync<InvalidDataException>(() => Frame.ReadAsync(stream, CancellationToken.None).AsTask());
        }

        [Theory]
        [InlineData(3)]
        [InlineData(5 + 9)]
        public async Task Read_OfAStreamThatEndsPartWay_IsNothing(int bytes)
        {
            byte[] written = [.. Header(10), .. new byte[10]];
            using MemoryStream stream = new MemoryStream(written[..bytes]);

            Assert.Null(await Frame.ReadAsync(stream, CancellationToken.None));
        }

        private static byte[] Header(int length)
        {
            byte[] header = new byte[5];
            header[0] = (byte)FrameType.Answer;
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1), length);
            return header;
        }
    }
}
