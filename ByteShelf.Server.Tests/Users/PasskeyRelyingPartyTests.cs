using ByteShelf.Server.Shared;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelf.Server.Users.Services;
using EasyReasy.Auth;

namespace ByteShelf.Server.Tests.Users
{
    public class PasskeyRelyingPartyTests
    {
        private readonly RecordingLogger _logger = new RecordingLogger();

        [Fact]
        public void For_HttpsPublicAddress_IsItsHostAtItsOrigin()
        {
            WebAuthnRelyingParty? relyingParty = PasskeyRelyingParty.For(new PublicAddress("https://shelf.example.com/byteshelf/"), _logger).RelyingParty;

            Assert.Equal("shelf.example.com", relyingParty?.Id);
            Assert.Equal(new[] { "https://shelf.example.com" }, relyingParty?.Origins);
            Assert.Empty(_logger.Messages);
        }

        [Fact]
        public void For_NoPublicAddress_IsOffWithoutAWord()
        {
            Assert.Null(PasskeyRelyingParty.For(new PublicAddress(null), _logger).RelyingParty);
            Assert.Empty(_logger.Messages);
        }

        [Fact]
        public void For_PlainHttpToAnotherMachine_IsOffWithAWarning()
        {
            Assert.Null(PasskeyRelyingParty.For(new PublicAddress("http://shelf.lan:5190/"), _logger).RelyingParty);
            Assert.Contains(_logger.Messages, message => message.Contains("Passkeys are off", StringComparison.Ordinal));
        }
    }
}
