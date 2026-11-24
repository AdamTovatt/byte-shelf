using System.Security.Cryptography;
using ByteShelf.Server.Plugins.Processes;

namespace ByteShelf.Server.Tests.Plugins.Processes
{
    /// <summary>Contents as a plugin's process reads them.</summary>
    public sealed class PipedContentsTests
    {
        private readonly byte[] _bytes = RandomNumberGenerator.GetBytes(3 * Frame.MaxPayloadBytes + 12345);
        private readonly List<ReadRequest> _asked = new List<ReadRequest>();

        [Fact]
        public async Task CopyTo_AnotherStream_IsTheWholeOfThem()
        {
            using PipedContents contents = Open();
            using MemoryStream copy = new MemoryStream();

            await contents.CopyToAsync(copy);

            Assert.Equal(_bytes, copy.ToArray());
            Assert.All(_asked, part => Assert.InRange(part.Count, 1, Frame.MaxPayloadBytes));
        }

        [Fact]
        public void Read_HereAndThere_AsksForLittleMoreThanIsRead()
        {
            using PipedContents contents = Open();
            byte[] part = new byte[100];

            contents.Seek(-100, SeekOrigin.End);
            contents.ReadExactly(part);
            Assert.Equal(_bytes[^100..], part);
            contents.Position = 5;
            contents.ReadExactly(part);
            contents.ReadExactly(part);
            Assert.Equal(_bytes[105..205], part);

            Assert.Equal(2, _asked.Count);
            Assert.True(_asked.Sum(request => request.Count) < 200_000);
        }

        [Fact]
        public void Read_PastTheEnd_IsNothing()
        {
            using PipedContents contents = Open();
            contents.Position = contents.Length + 10;

            Assert.Equal(0, contents.Read(new byte[10]));
            Assert.Throws<ArgumentOutOfRangeException>(() => contents.Seek(-1, SeekOrigin.Begin));
        }

        [Fact]
        public void Read_ContentsThatEndBeforeTheirLength_Throws()
        {
            using PipedContents contents = new PipedContents(100, part => new byte[part.Count - 1]);

            Assert.Throws<EndOfStreamException>(() => contents.Read(new byte[10]));
        }

        [Fact]
        public void Read_OnceDisposed_AsksForNothing()
        {
            PipedContents contents = Open();
            contents.Dispose();

            Assert.Throws<ObjectDisposedException>(() => contents.Read(new byte[10]));
            Assert.Empty(_asked);
        }

        private PipedContents Open()
        {
            return new PipedContents(_bytes.Length, request =>
            {
                _asked.Add(request);
                return _bytes.AsSpan((int)request.Offset, request.Count).ToArray();
            });
        }
    }
}
