using System.Net;
using System.Net.Http.Headers;

namespace ByteShelf.Plugins.Video.Tests
{
    public sealed class RangeServerTests
    {
        private static readonly byte[] Contents = [.. Enumerable.Range(0, 300_000).Select(index => (byte)(index % 251))];

        [Theory]
        [InlineData("bytes=0-", 0, 99)]
        [InlineData("bytes=10-19", 10, 19)]
        [InlineData("bytes=90-500", 90, 99)]
        [InlineData("bytes=99-99", 99, 99)]
        public void RangeOf_OnePart_IsItsBytesWithinTheContents(string range, long start, long end)
        {
            Assert.Equal((start, end), RangeServer.RangeOf(range, 100));
        }

        [Theory]
        [InlineData("bytes=100-")]
        [InlineData("bytes=20-10")]
        [InlineData("bytes=-10")]
        [InlineData("bytes=0-1,5-6")]
        [InlineData("items=0-1")]
        [InlineData("bytes=a-")]
        [InlineData("bytes=+1-")]
        public void RangeOf_AnyOther_IsNone(string range)
        {
            Assert.Null(RangeServer.RangeOf(range, 100));
        }

        [Fact]
        public async Task Get_ARange_AnswersItsBytes()
        {
            await using RangeServer server = RangeServer.Start(new MemoryStream(Contents));
            using HttpClient client = new HttpClient();
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, server.Url);
            request.Headers.Range = new RangeHeaderValue(70_000, 200_000);

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal("bytes 70000-200000/300000", response.Content.Headers.ContentRange!.ToString());
            Assert.Equal(Contents[70_000..200_001], await response.Content.ReadAsByteArrayAsync());
        }

        [Fact]
        public async Task Get_NoRange_AnswersAllOfThem()
        {
            await using RangeServer server = RangeServer.Start(new MemoryStream(Contents));
            using HttpClient client = new HttpClient();

            using HttpResponseMessage response = await client.GetAsync(server.Url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(["bytes"], response.Headers.AcceptRanges);
            Assert.Equal(Contents, await response.Content.ReadAsByteArrayAsync());
        }

        [Fact]
        public async Task Get_PastTheEnd_IsNotSatisfiable()
        {
            await using RangeServer server = RangeServer.Start(new MemoryStream(Contents));
            using HttpClient client = new HttpClient();
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, server.Url);
            request.Headers.Range = new RangeHeaderValue(300_000, null);

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, response.StatusCode);
            Assert.Equal("bytes */300000", response.Content.Headers.ContentRange!.ToString());
        }

        [Fact]
        public async Task Get_AnotherAddress_IsNotFound()
        {
            await using RangeServer server = RangeServer.Start(new MemoryStream(Contents));
            using HttpClient client = new HttpClient();

            using HttpResponseMessage response = await client.GetAsync(new Uri(new Uri(server.Url), "/other"));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        /// <summary>ffmpeg asks by GET alone.</summary>
        [Theory]
        [InlineData("POST")]
        [InlineData("HEAD")]
        public async Task AnyButGet_IsNotAllowed(string method)
        {
            await using RangeServer server = RangeServer.Start(new MemoryStream(Contents));
            using HttpClient client = new HttpClient();
            using HttpRequestMessage request = new HttpRequestMessage(new HttpMethod(method), server.Url);

            using HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }

        [Fact]
        public async Task Url_IsOnTheLoopbackAtAnAddressOfItsOwn()
        {
            await using RangeServer first = RangeServer.Start(new MemoryStream(Contents));
            await using RangeServer second = RangeServer.Start(new MemoryStream(Contents));
            Uri[] urls = [new Uri(first.Url), new Uri(second.Url)];

            Assert.All(urls, url => Assert.Equal("127.0.0.1", url.Host));
            Assert.Equal(64, urls[0].AbsolutePath.Length - 1);
            Assert.NotEqual(urls[0].AbsolutePath, urls[1].AbsolutePath);
        }

        [Fact]
        public async Task Get_ManyAtOnce_EachAnswersItsOwnBytes()
        {
            await using RangeServer server = RangeServer.Start(new MemoryStream(Contents));
            using HttpClient client = new HttpClient();

            byte[][] answers = await Task.WhenAll(Enumerable.Range(0, 8).Select(async part =>
            {
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, server.Url);
                request.Headers.Range = new RangeHeaderValue(part * 30_000, (part * 30_000) + 59_999);
                using HttpResponseMessage response = await client.SendAsync(request);
                return await response.Content.ReadAsByteArrayAsync();
            }));

            Assert.All(Enumerable.Range(0, 8), part => Assert.Equal(Contents[(part * 30_000)..((part * 30_000) + 60_000)], answers[part]));
        }

        /// <summary>ffmpeg hangs up on an answer as it seeks.</summary>
        [Fact]
        public async Task Get_HungUpOn_GoesOnAnsweringOthers()
        {
            await using RangeServer server = RangeServer.Start(new MemoryStream(Contents));
            using (HttpClient hungUp = new HttpClient())
            {
                using HttpResponseMessage response = await hungUp.GetAsync(server.Url, HttpCompletionOption.ResponseHeadersRead);
                await using Stream body = await response.Content.ReadAsStreamAsync();
                await body.ReadExactlyAsync(new byte[10]);
            }

            using HttpClient client = new HttpClient();
            Assert.Equal(Contents, await client.GetByteArrayAsync(server.Url));
        }
    }
}
