using System.Buffers.Text;
using ByteShelf.Server.Shared.Auth;
using ByteShelf.Server.Tests.TestUtilities;

namespace ByteShelf.Server.Tests.Shared
{
    /// <summary>The signed tokens that name a link's row.</summary>
    public sealed class LinkTokensTests
    {
        [Fact]
        public async Task TryRead_ItsOwnKind_AnswersTheLink()
        {
            await using TestRoot root = TestRoot.Create();
            LinkTokens tokens = new LinkTokens(root.Root);
            Guid linkId = Guid.NewGuid();

            bool read = tokens.TryRead("download-link", tokens.Create("download-link", linkId), out Guid readId);

            Assert.Equal((true, linkId), (read, readId));
        }

        [Fact]
        public async Task TryRead_AnotherKind_IsRefused()
        {
            await using TestRoot root = TestRoot.Create();
            LinkTokens tokens = new LinkTokens(root.Root);

            Assert.False(tokens.TryRead("upload-link", tokens.Create("download-link", Guid.NewGuid()), out _));
        }

        [Fact]
        public async Task TryRead_FromAnotherRoot_IsRefused()
        {
            await using TestRoot first = TestRoot.Create();
            await using TestRoot second = TestRoot.Create();
            string token = new LinkTokens(first.Root).Create("download-link", Guid.NewGuid());

            Assert.False(new LinkTokens(second.Root).TryRead("download-link", token, out _));
        }

        [Fact]
        public async Task TryRead_AfterARestartOnTheSameRoot_ReadsEarlierTokens()
        {
            await using TestRoot root = TestRoot.Create();
            Guid linkId = Guid.NewGuid();
            string token = new LinkTokens(root.Root).Create("download-link", linkId);

            bool read = new LinkTokens(root.Root).TryRead("download-link", token, out Guid readId);

            Assert.Equal((true, linkId), (read, readId));
        }

        // A token is the link's 16-byte id, then a 32-byte signature: the id's first byte, a signature byte, the last byte.
        [Theory]
        [InlineData(0)]
        [InlineData(20)]
        [InlineData(47)]
        public async Task TryRead_AnyByteChanged_IsRefused(int index)
        {
            await using TestRoot root = TestRoot.Create();
            LinkTokens tokens = new LinkTokens(root.Root);
            byte[] bytes = Base64Url.DecodeFromChars(tokens.Create("download-link", Guid.NewGuid()));
            bytes[index] ^= 1;

            Assert.False(tokens.TryRead("download-link", Base64Url.EncodeToString(bytes), out _));
        }

        [Theory]
        [InlineData("")]
        [InlineData("not-a-token")]
        [InlineData("AAAA")]
        [InlineData("*")]
        public async Task TryRead_NotAToken_IsRefused(string token)
        {
            await using TestRoot root = TestRoot.Create();

            Assert.False(new LinkTokens(root.Root).TryRead("download-link", token, out _));
        }

        [Fact]
        public async Task TryRead_SignedTokenWithABytePastIt_IsRefused()
        {
            await using TestRoot root = TestRoot.Create();
            LinkTokens tokens = new LinkTokens(root.Root);
            byte[] bytes = Base64Url.DecodeFromChars(tokens.Create("download-link", Guid.NewGuid()));

            Assert.False(tokens.TryRead("download-link", Base64Url.EncodeToString([.. bytes, 0]), out _));
        }
    }
}
