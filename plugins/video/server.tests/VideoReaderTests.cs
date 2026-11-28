using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using ByteShelf.Plugins.Video.Tests.TestUtilities;

namespace ByteShelf.Plugins.Video.Tests
{
    public sealed class VideoReaderTests
    {
        [FfmpegFact]
        public async Task ReadAsync_AVideo_SaysHowLongAndLargeItIsAndItsStoryboard()
        {
            JsonObject document = (await ReadAsync(await TestVideos.StreamingWellAsync()))!;

            Assert.Equal(6, document["durationSeconds"]!.GetValue<double>(), precision: 1);
            Assert.Equal(320, document["width"]!.GetValue<int>());
            Assert.Equal(240, document["height"]!.GetValue<int>());
            Storyboard expected = Storyboard.For(document["durationSeconds"]!.GetValue<double>(), 320, 240);
            JsonObject storyboard = document["storyboard"]!.AsObject();
            Assert.Equal(expected.IntervalSeconds, storyboard["intervalSeconds"]!.GetValue<double>());
            Assert.Equal(expected.Columns, storyboard["columns"]!.GetValue<int>());
            Assert.Equal(expected.Rows, storyboard["rows"]!.GetValue<int>());
            Assert.Equal(expected.FrameWidth, storyboard["frameWidth"]!.GetValue<int>());
            Assert.Equal(expected.FrameHeight, storyboard["frameHeight"]!.GetValue<int>());
            Assert.Equal(expected.Count, storyboard["count"]!.GetValue<int>());
        }

        [FfmpegFact]
        public async Task ReadAsync_AVideoThatStreamsWell_SaysSo()
        {
            JsonObject document = (await ReadAsync(await TestVideos.StreamingWellAsync()))!;

            Assert.True(document["streamsWell"]!.GetValue<bool>());
        }

        [FfmpegFact]
        public async Task ReadAsync_SparseKeyframes_DoNotStreamWell()
        {
            JsonObject document = (await ReadAsync(await TestVideos.SparseKeyframesAsync()))!;

            Assert.False(document["streamsWell"]!.GetValue<bool>());
        }

        [FfmpegFact]
        public async Task ReadAsync_AnIndexAtTheEnd_DoesNotStreamWell()
        {
            JsonObject document = (await ReadAsync(await TestVideos.IndexAtEndAsync()))!;

            Assert.False(document["streamsWell"]!.GetValue<bool>());
        }

        [FfmpegFact]
        public async Task ReadAsync_Matroska_IsReadAndDoesNotStreamWell()
        {
            JsonObject document = (await ReadAsync(await TestVideos.MatroskaAsync()))!;

            Assert.Equal(320, document["width"]!.GetValue<int>());
            Assert.False(document["streamsWell"]!.GetValue<bool>());
        }

        /// <summary>A phone held upright records its pixels on their side and says to turn them.</summary>
        [FfmpegFact]
        public async Task ReadAsync_ATurnedVideo_SaysItsSizeUpright()
        {
            JsonObject document = (await ReadAsync(await TestVideos.TurnedAsync(await TestVideos.StreamingWellAsync(), 90)))!;

            Assert.Equal(240, document["width"]!.GetValue<int>());
            Assert.Equal(320, document["height"]!.GetValue<int>());
        }

        [FfmpegFact]
        public async Task ReadAsync_NoVideo_SaysNothing()
        {
            Assert.Null(await ReadAsync(Encoding.UTF8.GetBytes("Not a video at all, though it is stored as one.")));
        }

        [FfmpegFact]
        public async Task ReadAsync_SoundAlone_SaysNothing()
        {
            byte[] sound = await TestVideos.MakeAsync(".m4a", "-f", "lavfi", "-i", "sine=frequency=440:duration=2", "-c:a", "aac");

            Assert.Null(await ReadAsync(sound));
        }

        /// <summary>
        /// ffmpeg would open what a playlist names: another server, or a file of the machine's. A DASH manifest it takes by
        /// what it says alone (an HLS playlist only by its address's extension or its media type, which the contents'
        /// address has neither of).
        /// </summary>
        [FfmpegFact]
        public async Task ReadAsync_APlaylist_IsNoVideoAndNothingItNamesIsOpened()
        {
            using TcpListener named = new TcpListener(IPAddress.Loopback, 0);
            named.Start();
            int port = ((IPEndPoint)named.LocalEndpoint).Port;
            int opened = 0;
            using CancellationTokenSource done = new CancellationTokenSource();
            Task answering = Task.Run(async () =>
            {
                // Each is hung up on at once, so ffmpeg ends rather than waits for an answer.
                while (!done.IsCancellationRequested)
                {
                    using TcpClient client = await named.AcceptTcpClientAsync(done.Token);
                    Interlocked.Increment(ref opened);
                }
            });
            string manifest = $"""
                <?xml version="1.0"?>
                <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" profiles="urn:mpeg:dash:profile:isoff-on-demand:2011" type="static" mediaPresentationDuration="PT10S" minBufferTime="PT1S">
                  <Period><AdaptationSet mimeType="video/mp4"><Representation id="1" bandwidth="1000" codecs="avc1.64001f" width="320" height="240">
                    <BaseURL>http://127.0.0.1:{port}/part.mp4</BaseURL><SegmentBase indexRange="0-100"/>
                  </Representation></AdaptationSet></Period>
                </MPD>
                """;

            JsonObject? document = await ReadAsync(Encoding.UTF8.GetBytes(manifest)).WaitAsync(TimeSpan.FromSeconds(30));
            await done.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => answering);

            Assert.Null(document);
            Assert.Equal(0, opened);
        }

        private static async Task<JsonObject?> ReadAsync(byte[] contents)
        {
            using MemoryStream stream = new MemoryStream(contents, writable: false);
            return await new VideoReader().ReadAsync(stream);
        }
    }
}
