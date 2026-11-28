namespace ByteShelf.Plugins.Video
{
    /// <summary>
    /// Whether a video streams well as it is, so no <c>stream</c> is made of it and the player plays it: the one rule of
    /// it, which the maker and the reader's <c>streamsWell</c> go by. It is an MP4 whose index is at the front, of H.264
    /// that every browser decodes, with AAC or no audio, a keyframe at least every 2 seconds and at most 8 Mbps in all.
    /// </summary>
    internal static class StreamsWell
    {
        internal const double MaxKeyframeGapSeconds = 2;

        internal const double MaxBitsPerSecond = 8_000_000;

        /// <summary>H.264's profiles of 8-bit 4:2:0, which every browser decodes; High 10 and 4:2:2 or 4:4:4 are not.</summary>
        private static readonly string[] PlayedProfiles = ["Constrained Baseline", "Baseline", "Main", "High"];

        private static readonly string[] PlayedPixelFormats = ["yuv420p", "yuvj420p"];

        /// <summary>
        /// A QuickTime file, which is what a phone's <c>.mov</c> is, is stored as <c>video/quicktime</c>, which some
        /// browsers do not play whatever is in it.
        /// </summary>
        private const string QuickTime = "qt  ";

        public static bool Of(VideoProbe video)
        {
            return PlaysAsItIs(video)
                && video.Index is { IndexAtFront: true, MaxKeyframeGapSeconds: <= MaxKeyframeGapSeconds }
                && video.BitsPerSecond <= MaxBitsPerSecond;
        }

        /// <summary>Whether every browser plays the video as it is, however it streams: an MP4 of H.264 that each decodes, with AAC or no audio.</summary>
        public static bool PlaysAsItIs(VideoProbe video)
        {
            return video.Index != null
                && video.MajorBrand != QuickTime
                && video.VideoCodec == "h264"
                && PlayedProfiles.Contains(video.VideoProfile)
                && PlayedPixelFormats.Contains(video.PixelFormat)
                && video.AudioCodecs.All(codec => codec == "aac");
        }
    }
}
