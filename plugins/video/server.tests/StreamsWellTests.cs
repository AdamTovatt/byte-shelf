namespace ByteShelf.Plugins.Video.Tests
{
    public sealed class StreamsWellTests
    {
        /// <summary>10 seconds of 10 MB: exactly 8 Mbps.</summary>
        private static readonly VideoProbe Streaming = new VideoProbe(
            DurationSeconds: 10,
            Width: 1920,
            Height: 1080,
            VideoCodec: "h264",
            VideoProfile: "High",
            PixelFormat: "yuv420p",
            AudioCodecs: ["aac"],
            MajorBrand: "isom",
            Index: new Mp4Index(IndexAtFront: true, MaxKeyframeGapSeconds: 2),
            Bytes: 10_000_000);

        private static readonly Dictionary<string, VideoProbe> Others = new Dictionary<string, VideoProbe>
        {
            ["no MP4"] = Streaming with { Index = null },
            ["its index at the end"] = Streaming with { Index = new Mp4Index(IndexAtFront: false, MaxKeyframeGapSeconds: null) },
            ["keyframes it cannot tell"] = Streaming with { Index = new Mp4Index(IndexAtFront: true, MaxKeyframeGapSeconds: null) },
            ["keyframes over 2 seconds apart"] = Streaming with { Index = new Mp4Index(IndexAtFront: true, MaxKeyframeGapSeconds: 2.01) },
            ["QuickTime"] = Streaming with { MajorBrand = "qt  " },
            ["HEVC"] = Streaming with { VideoCodec = "hevc", VideoProfile = "Main" },
            ["High 10"] = Streaming with { VideoProfile = "High 10" },
            ["4:2:2"] = Streaming with { PixelFormat = "yuv422p" },
            ["10-bit pixels"] = Streaming with { PixelFormat = "yuv420p10le" },
            ["other audio"] = Streaming with { AudioCodecs = ["aac", "ac3"] },
            ["over 8 Mbps"] = Streaming with { Bytes = 10_000_001 },
        };

        public static TheoryData<string> NotStreaming => [.. Others.Keys];

        [Fact]
        public void Of_H264AndAacWithItsIndexAtTheFrontAndKeyframesOften_StreamsWell()
        {
            Assert.True(StreamsWell.Of(Streaming));
        }

        [Fact]
        public void Of_NoAudio_StreamsWell()
        {
            Assert.True(StreamsWell.Of(Streaming with { AudioCodecs = [] }));
        }

        [Theory]
        [InlineData("Constrained Baseline", "yuvj420p")]
        [InlineData("Baseline", "yuv420p")]
        [InlineData("Main", "yuv420p")]
        public void Of_ProfilesEveryBrowserDecodes_StreamWell(string profile, string pixels)
        {
            Assert.True(StreamsWell.Of(Streaming with { VideoProfile = profile, PixelFormat = pixels }));
        }

        [Theory]
        [MemberData(nameof(NotStreaming))]
        public void Of_AnyOtherVideo_DoesNotStreamWell(string what)
        {
            Assert.False(StreamsWell.Of(Others[what]));
        }
    }
}
