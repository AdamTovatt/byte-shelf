namespace ByteShelf.Plugins.Video.Tests
{
    public sealed class VideoProbeTests
    {
        [Fact]
        public void Parse_AVideo_SaysWhatItIs()
        {
            VideoProbe probe = Parse(
                """{"codec_type":"video","codec_name":"h264","profile":"High","pix_fmt":"yuv420p","width":1920,"height":1080},{"codec_type":"audio","codec_name":"aac"},{"codec_type":"audio","codec_name":"opus"}""",
                """{"duration":"12.5","tags":{"major_brand":"isom"}}""")!;

            Assert.Equivalent(new VideoProbe(12.5, 1920, 1080, "h264", "High", "yuv420p", ["aac", "opus"], "isom", Index: null, Bytes: 1000), probe, strict: true);
        }

        [Theory]
        [InlineData(90)]
        [InlineData(-90)]
        [InlineData(270)]
        public void Parse_TurnedOnItsSide_IsUpright(int rotation)
        {
            VideoProbe probe = Parse(
                $$"""{"codec_type":"video","codec_name":"h264","width":1920,"height":1080,"side_data_list":[{"side_data_type":"Display Matrix","rotation":{{rotation}}}]}""",
                """{"duration":"1"}""")!;

            Assert.Equal((1080, 1920), (probe.Width, probe.Height));
        }

        [Fact]
        public void Parse_UpsideDown_IsAsWideAsItIs()
        {
            VideoProbe probe = Parse(
                """{"codec_type":"video","codec_name":"h264","width":1920,"height":1080,"side_data_list":[{"rotation":180}]}""",
                """{"duration":"1"}""")!;

            Assert.Equal((1920, 1080), (probe.Width, probe.Height));
        }

        /// <summary>Anamorphic DVD video: 720 by 576 pixels shown 16:9.</summary>
        [Fact]
        public void Parse_PixelsNotSquare_IsAsWideAsItIsShown()
        {
            VideoProbe probe = Parse(
                """{"codec_type":"video","codec_name":"mpeg2video","width":720,"height":576,"sample_aspect_ratio":"64:45"}""",
                """{"duration":"1"}""")!;

            Assert.Equal((1024, 576), (probe.Width, probe.Height));
        }

        [Fact]
        public void Parse_ACoversPicture_IsNoVideo()
        {
            Assert.Null(Parse(
                """{"codec_type":"audio","codec_name":"aac"},{"codec_type":"video","codec_name":"mjpeg","width":500,"height":500,"disposition":{"attached_pic":1}}""",
                """{"duration":"200"}"""));
        }

        [Fact]
        public void Parse_ACoverAndAVideo_IsTheVideo()
        {
            VideoProbe probe = Parse(
                """{"codec_type":"video","codec_name":"mjpeg","width":500,"height":500,"disposition":{"attached_pic":1}},{"codec_type":"video","codec_name":"h264","width":640,"height":360}""",
                """{"duration":"3"}""")!;

            Assert.Equal(("h264", 640), (probe.VideoCodec, probe.Width));
        }

        [Fact]
        public void Parse_NoDurationOfTheFormat_TakesTheVideos()
        {
            VideoProbe probe = Parse("""{"codec_type":"video","codec_name":"vp9","width":640,"height":360,"duration":"4.25"}""", "{}")!;

            Assert.Equal(4.25, probe.DurationSeconds);
        }

        [Theory]
        [InlineData("""{"duration":"0"}""")]
        [InlineData("""{"duration":"N/A"}""")]
        [InlineData("{}")]
        public void Parse_NoTimeItPlays_IsNoVideo(string format)
        {
            Assert.Null(Parse("""{"codec_type":"video","codec_name":"h264","width":640,"height":360}""", format));
        }

        [Fact]
        public void Parse_NoSize_IsNoVideo()
        {
            Assert.Null(Parse("""{"codec_type":"video","codec_name":"h264","width":0,"height":360}""", """{"duration":"1"}"""));
        }

        [Fact]
        public void BitsPerSecond_AreOverTheWholeOfIt()
        {
            VideoProbe probe = Parse("""{"codec_type":"video","codec_name":"h264","width":640,"height":360}""", """{"duration":"8"}""")!;

            Assert.Equal(1000, probe.BitsPerSecond);
        }

        private static VideoProbe? Parse(string streams, string format)
        {
            return VideoProbe.Parse($$"""{"streams":[{{streams}}],"format":{{format}}}""", bytes: 1000);
        }
    }
}
