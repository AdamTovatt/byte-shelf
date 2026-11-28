using System.Buffers.Binary;
using ByteShelf.Plugins.Video.Tests.TestUtilities;

namespace ByteShelf.Plugins.Video.Tests
{
    public sealed class Mp4IndexTests
    {
        [Fact]
        public void GapOf_KeyframesApart_IsTheMostTimeBetweenThem()
        {
            // 50 samples of a tenth of a second, keyframes at 0, 1, 3.5 and 4 seconds of 5.
            Assert.Equal(2.5, Mp4Index.GapOf([(50, 100)], [1, 11, 36, 41], timescale: 1000));
        }

        [Fact]
        public void GapOf_TheLastKeyframeFarFromTheEnd_IsThatTime()
        {
            Assert.Equal(9, Mp4Index.GapOf([(100, 100)], [1, 11], timescale: 1000));
        }

        [Fact]
        public void GapOf_TheFirstKeyframeLate_IsTheTimeBeforeIt()
        {
            Assert.Equal(5, Mp4Index.GapOf([(100, 100)], [51, 61, 71, 81, 91], timescale: 1000));
        }

        [Fact]
        public void GapOf_RunsOfOtherDurations_GoByEach()
        {
            // 10 samples of a second, then 10 of half; keyframes at 0, 10 and 12.5 seconds.
            Assert.Equal(10, Mp4Index.GapOf([(10, 1000), (10, 500)], [16, 1, 11], timescale: 1000));
        }

        [Fact]
        public void GapOf_NoSyncTable_EverySampleIsAKeyframe()
        {
            Assert.Equal(0.5, Mp4Index.GapOf([(10, 100), (5, 500)], sync: null, timescale: 1000));
        }

        [Fact]
        public void GapOf_NoSamples_IsNone()
        {
            Assert.Null(Mp4Index.GapOf([], sync: null, timescale: 1000));
        }

        [Fact]
        public void GapOf_KeyframesOfNoSample_AreNone()
        {
            Assert.Null(Mp4Index.GapOf([(10, 100)], [0, 11], timescale: 1000));
        }

        [Fact]
        public void HeaderOf_ASizeIn32Bits_IsThatSize()
        {
            Assert.Equal(("moov", 8, 24L), Mp4Index.HeaderOf([0, 0, 0, 24, .. "moov"u8], room: 100));
        }

        [Fact]
        public void HeaderOf_ASizeOf1_IsSaidIn64BitsAfterTheType()
        {
            Assert.Equal(("mdat", 16, 0x1_0000_0010L), Mp4Index.HeaderOf([0, 0, 0, 1, .. "mdat"u8, 0, 0, 0, 1, 0, 0, 0, 0x10], room: 0x2_0000_0000));
        }

        [Fact]
        public void HeaderOf_ASizeOf0_IsAllTheRoom()
        {
            Assert.Equal(("mdat", 8, 100L), Mp4Index.HeaderOf([0, 0, 0, 0, .. "mdat"u8], room: 100));
        }

        [Theory]
        [InlineData(new byte[] { 0, 0, 0, 24, (byte)'m', (byte)'o', (byte)'o' })]
        [InlineData(new byte[] { 0, 0, 0, 1, (byte)'m', (byte)'d', (byte)'a', (byte)'t', 0, 0, 0, 0 })]
        [InlineData(new byte[] { 0, 0, 0, 101, (byte)'m', (byte)'o', (byte)'o', (byte)'v' })]
        [InlineData(new byte[] { 0, 0, 0, 7, (byte)'m', (byte)'o', (byte)'o', (byte)'v' })]
        public void HeaderOf_CutShortOrOfASizeThatDoesNotFit_IsNone(byte[] header)
        {
            Assert.Null(Mp4Index.HeaderOf(header, room: 100));
        }

        /// <summary>Its index first, then a video's, whose keyframes are 5 seconds apart.</summary>
        [FfmpegFact]
        public async Task ReadAsync_SoundBeforeTheVideo_IsOfTheVideo()
        {
            byte[] video = await TestVideos.MakeAsync(
                ".mp4", "-f", "lavfi", "-i", "sine=frequency=440:duration=10", "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=30:duration=10",
                "-map", "0:a", "-map", "1:v", "-c:a", "aac", "-c:v", "libx264", "-g", "150", "-sc_threshold", "0", "-movflags", "+faststart");

            Mp4Index index = (await ReadAsync(video))!;

            Assert.Equal(5, index.MaxKeyframeGapSeconds!.Value, precision: 2);
        }

        /// <summary>A file that says so much, of zeros past its headers, sparse on disk; of which no more than a block is read.</summary>
        [Fact]
        public async Task ReadAsync_AnIndexLargerThanItTakes_IsAtTheFrontWithKeyframesItCannotTellAndIsNotRead()
        {
            string path = Path.GetTempFileName();
            try
            {
                const long moovBytes = 16 + Mp4Index.MaxIndexBytes + 1;
                await using (FileStream file = File.Create(path))
                {
                    file.Write([0, 0, 0, 16, .. "ftyp"u8, .. "isom"u8, 0, 0, 0, 0]);
                    file.Write([0, 0, 0, 1, .. "moov"u8]);
                    byte[] size = new byte[8];
                    BinaryPrimitives.WriteInt64BigEndian(size, moovBytes);
                    file.Write(size);
                    file.SetLength(16 + moovBytes);
                }

                using CountingStream contents = new CountingStream(File.OpenRead(path));
                using BlockCache cache = new BlockCache(contents);
                Assert.Equal(new Mp4Index(IndexAtFront: true, MaxKeyframeGapSeconds: null), await Mp4Index.ReadAsync(cache, CancellationToken.None));
                Assert.InRange(contents.BytesRead, 1, BlockCache.BlockBytes);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [FfmpegFact]
        public async Task ReadAsync_IndexAtTheFront_SaysSoAndHowFarApartKeyframesAre()
        {
            Mp4Index index = (await ReadAsync(await TestVideos.StreamingWellAsync()))!;

            Assert.True(index.IndexAtFront);
            Assert.Equal(1, index.MaxKeyframeGapSeconds!.Value, precision: 2);
        }

        [FfmpegFact]
        public async Task ReadAsync_SparseKeyframes_SaysHowFarApart()
        {
            Mp4Index index = (await ReadAsync(await TestVideos.SparseKeyframesAsync()))!;

            Assert.Equal(5, index.MaxKeyframeGapSeconds!.Value, precision: 2);
        }

        [FfmpegFact]
        public async Task ReadAsync_IndexAtTheEnd_SaysSo()
        {
            Assert.Equal(new Mp4Index(IndexAtFront: false, MaxKeyframeGapSeconds: null), await ReadAsync(await TestVideos.IndexAtEndAsync()));
        }

        /// <summary>A fragmented MP4's samples are in its fragments, not its index.</summary>
        [FfmpegFact]
        public async Task ReadAsync_Fragmented_HasKeyframesItCannotTell()
        {
            byte[] fragmented = await TestVideos.Mp4Async("testsrc2=size=320x240:rate=30:duration=4", "-c:v", "libx264", "-g", "30", "-movflags", "frag_keyframe+empty_moov");

            Assert.Equal(new Mp4Index(IndexAtFront: true, MaxKeyframeGapSeconds: null), await ReadAsync(fragmented));
        }

        [FfmpegFact]
        public async Task ReadAsync_Matroska_IsNone()
        {
            Assert.Null(await ReadAsync(await TestVideos.MatroskaAsync()));
        }

        [Fact]
        public async Task ReadAsync_BoxesThatRunPastTheEnd_AreNone()
        {
            byte[] broken = [0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', 0, 0, 0, 0];

            Assert.Null(await ReadAsync(broken));
        }

        [Fact]
        public async Task ReadAsync_Nothing_IsNone()
        {
            Assert.Null(await ReadAsync([]));
        }

        private static async Task<Mp4Index?> ReadAsync(byte[] contents)
        {
            using BlockCache cache = new BlockCache(new MemoryStream(contents, writable: false));
            return await Mp4Index.ReadAsync(cache, CancellationToken.None);
        }
    }
}
