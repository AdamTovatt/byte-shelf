using ByteShelf.Server.Plugins.Processes;

namespace ByteShelf.Server.Tests.Plugins.Processes
{
    /// <summary>What part of the contents a plugin's process may ask for.</summary>
    public sealed class ReadRequestTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(long.MaxValue, Frame.MaxPayloadBytes)]
        public void From_OfWhatItWroteToPayload_IsTheSameRequest(long offset, int count)
        {
            ReadRequest request = new ReadRequest(offset, count);

            Assert.Equal(request, ReadRequest.From(request.ToPayload()));
        }

        [Theory]
        [InlineData(-1, 1)]
        [InlineData(0, -1)]
        [InlineData(0, Frame.MaxPayloadBytes + 1)]
        public void From_ARequestForNoBytesThatCanBeAnswered_IsRefused(long offset, int count)
        {
            Assert.Throws<InvalidDataException>(() => ReadRequest.From(new ReadRequest(offset, count).ToPayload()));
        }

        [Theory]
        [InlineData(11)]
        [InlineData(13)]
        public void From_APayloadOfAnotherLength_IsRefused(int length)
        {
            Assert.Throws<InvalidDataException>(() => ReadRequest.From(new byte[length]));
        }
    }
}
