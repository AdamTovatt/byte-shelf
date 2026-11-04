using System.Buffers.Text;
using System.Text;
using ByteShelf.Server.Replication.Models;

namespace ByteShelf.Server.Tests.Replication
{
    /// <summary>What an admin pastes: a code made by <see cref="PairingCode.ToString"/>, or anything else.</summary>
    public class PairingCodeTests
    {
        [Fact]
        public void TryParse_ACodeAsWritten_ReadsItBack()
        {
            PairingCode code = new PairingCode { PrimaryUrl = "https://shelf.example.com", InstanceId = "abc", Secret = "s3cret" };

            PairingCode? parsed = PairingCode.TryParse($"  {code}\n");

            Assert.Equal((code.PrimaryUrl, code.InstanceId, code.Secret), (parsed?.PrimaryUrl, parsed?.InstanceId, parsed?.Secret));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("bsrep2_eyJ9")]
        [InlineData("bsrep1_not*base64")]
        [InlineData("bsrep1_bm90IGpzb24")]
        public void TryParse_NotACode_IsNull(string? text)
        {
            Assert.Null(PairingCode.TryParse(text));
        }

        [Theory]
        [InlineData("""{"primaryUrl":"ftp://shelf.example.com","instanceId":"abc","secret":"s"}""")]
        [InlineData("""{"primaryUrl":"/relative","instanceId":"abc","secret":"s"}""")]
        [InlineData("""{"primaryUrl":"https://shelf.example.com","instanceId":"","secret":"s"}""")]
        [InlineData("""{"primaryUrl":"https://shelf.example.com","instanceId":"abc","secret":""}""")]
        [InlineData("""{"primaryUrl":"https://shelf.example.com","instanceId":null,"secret":"s"}""")]
        [InlineData("""{"primaryUrl":"https://shelf.example.com","secret":"s"}""")]
        public void TryParse_ACodeThatCannotBeUsed_IsNull(string json)
        {
            string text = PairingCode.Prefix + Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));

            Assert.Null(PairingCode.TryParse(text));
        }
    }
}
