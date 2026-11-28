using System.Globalization;
using System.Text;
using System.Text.Json;
using ByteShelf.Plugins.Video.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Plugins.Video.Tests
{
    public sealed class VideoMakerTests
    {
        [FfmpegFact]
        public async Task MakeAsync_Thumbnail_IsAWebPPosterOfAtMost320Pixels()
        {
            MadeFiles made = await MadeFiles.MakeAsync(await TestVideos.Mp4Async("testsrc2=size=1280x720:rate=30:duration=2", "-c:v", "libx264"), DerivedKind.Thumbnail);

            Assert.Equal("image/webp", made.MediaTypeOf(DerivedKind.Thumbnail));
            JsonElement poster = await TestVideos.VideoStreamAsync(made.BytesOf(DerivedKind.Thumbnail), ".webp");
            Assert.Equal(320, poster.GetProperty("width").GetInt32());
            Assert.Equal(180, poster.GetProperty("height").GetInt32());
        }

        /// <summary>Of 10 seconds, white from 0.8 to 1.5 and black else: a tenth in, it is white.</summary>
        [FfmpegFact]
        public async Task MakeAsync_Thumbnail_IsOfAFrameATenthIn()
        {
            byte[] video = await TestVideos.Mp4Async("color=c=black:size=160x120:rate=10:duration=10,drawbox=c=white:t=fill:enable='between(t,0.8,1.5)'", "-c:v", "libx264", "-g", "1");

            MadeFiles made = await MadeFiles.MakeAsync(video, DerivedKind.Thumbnail);

            Assert.True(await TestVideos.MeanGrayAsync(made.BytesOf(DerivedKind.Thumbnail)) > 200);
        }

        [FfmpegFact]
        public async Task MakeAsync_ATurnedVideo_HasItsPosterUpright()
        {
            byte[] video = await TestVideos.TurnedAsync(await TestVideos.StreamingWellAsync(), 90);

            MadeFiles made = await MadeFiles.MakeAsync(video, DerivedKind.Thumbnail);

            JsonElement poster = await TestVideos.VideoStreamAsync(made.BytesOf(DerivedKind.Thumbnail), ".webp");
            Assert.Equal(240, poster.GetProperty("width").GetInt32());
            Assert.Equal(320, poster.GetProperty("height").GetInt32());
        }

        [FfmpegFact]
        public async Task MakeAsync_Preview_IsAWebPGridOfTheStoryboardsLayout()
        {
            byte[] video = await TestVideos.StreamingWellAsync();

            MadeFiles made = await MadeFiles.MakeAsync(video, DerivedKind.Preview);

            Assert.Equal("image/webp", made.MediaTypeOf(DerivedKind.Preview));
            Storyboard storyboard = Storyboard.For(await TestVideos.DurationAsync(video, ".mp4"), 320, 240);
            JsonElement grid = await TestVideos.VideoStreamAsync(made.BytesOf(DerivedKind.Preview), ".webp");
            Assert.Equal(storyboard.Columns * storyboard.FrameWidth, grid.GetProperty("width").GetInt32());
            Assert.Equal(storyboard.Rows * storyboard.FrameHeight, grid.GetProperty("height").GetInt32());
        }

        /// <summary>Of 20 seconds, ten frames each of the middle of its 2 seconds: white from 6 to 8 and black else, the fourth alone is white.</summary>
        [FfmpegFact]
        public async Task MakeAsync_Preview_HasEachFrameInItsPlace()
        {
            byte[] video = await TestVideos.Mp4Async(Lit(6, 7.9), "-c:v", "libx264", "-g", "1", "-movflags", "+faststart");

            MadeFiles made = await MadeFiles.MakeAsync(video, DerivedKind.Preview);

            Assert.Equal([3], await WhiteFramesAsync(made.BytesOf(DerivedKind.Preview)));
        }

        /// <summary>
        /// Cut short after 1.5 seconds of 20, white for its first 2: the poster, a tenth in, cannot be made, which leaves the
        /// storyboard to be made all the same, each of its frames past the cut the nearest one taken, the first.
        /// </summary>
        [FfmpegFact]
        public async Task MakeAsync_AVideoCutShort_LeavesOutThePosterAloneAndFillsTheStoryboardFromWhatItHas()
        {
            byte[] video = await TestVideos.CutAsync(await TestVideos.Mp4Async(Lit(0, 1.9), "-c:v", "libx264", "-g", "1", "-movflags", "+faststart"), 1.5);

            MadeFiles made = await MadeFiles.MakeAsync(video, DerivedKind.Thumbnail, DerivedKind.Preview);

            Assert.Equal([DerivedKind.Preview], made.Kinds);
            Assert.Equal(Enumerable.Range(0, 10), await WhiteFramesAsync(made.BytesOf(DerivedKind.Preview)));
        }

        [FfmpegFact]
        public async Task MakeAsync_AVideoCutShortBeforeAnyFrame_MakesNothing()
        {
            byte[] video = await TestVideos.CutAsync(await TestVideos.Mp4Async(Lit(0, 1.9), "-c:v", "libx264", "-g", "1", "-movflags", "+faststart"), 0);

            MadeFiles made = await MadeFiles.MakeAsync(video, DerivedKind.Thumbnail, DerivedKind.Preview);

            Assert.Empty(made.Kinds);
        }

        /// <summary>
        /// Each of poster and storyboard seeks to the keyframes it takes and reads little else, so a long video is done in a
        /// quick run: of a video of 60 seconds at 8 Mbps with a keyframe every 5, under a fifteenth of its bytes. Decoding
        /// at each seek reads the frames up to its time too, and asking without an end reads ahead, each over the bound.
        /// </summary>
        [FfmpegFact]
        public async Task MakeAsync_PosterAndStoryboard_ReadLittleOfTheContents()
        {
            byte[] video = await TestVideos.Mp4Async("testsrc2=size=640x360:rate=30:duration=60,noise=alls=40:allf=t", "-c:v", "libx264", "-preset", "ultrafast", "-b:v", "8M", "-g", "150", "-movflags", "+faststart");
            using CountingStream contents = new CountingStream(video);

            MadeFiles made = await MadeFiles.MakeAsync(contents, DerivedKind.Thumbnail, DerivedKind.Preview);

            Assert.Equal([DerivedKind.Thumbnail, DerivedKind.Preview], made.Kinds);
            Assert.InRange(contents.BytesRead, 1, video.Length / 15);
        }

        [FfmpegFact]
        public async Task MakeAsync_Stream_IsAnMp4ThatStreamsWell()
        {
            byte[] video = await TestVideos.SparseKeyframesAsync();

            MadeFiles made = await MadeFiles.MakeAsync(video, DerivedKind.Stream);

            Assert.Equal("video/mp4", made.MediaTypeOf(DerivedKind.Stream));
            Assert.True(await StreamsWellAsync(made.BytesOf(DerivedKind.Stream)));
        }

        /// <summary>What every browser plays and seeks in quickly: H.264 High in 8-bit 4:2:0, a keyframe each second, AAC in stereo.</summary>
        [FfmpegFact]
        public async Task MakeAsync_Stream_IsH264HighWithAKeyframeEachSecondAndAac()
        {
            byte[] video = await TestVideos.SparseKeyframesAsync();

            MadeFiles made = await MadeFiles.MakeAsync(video, DerivedKind.Stream);

            byte[] stream = made.BytesOf(DerivedKind.Stream);
            using JsonDocument probe = await TestVideos.ProbeAsync(stream, ".mp4");
            JsonElement[] streams = [.. probe.RootElement.GetProperty("streams").EnumerateArray()];
            JsonElement picture = streams.Single(each => each.GetProperty("codec_type").GetString() == "video");
            JsonElement sound = streams.Single(each => each.GetProperty("codec_type").GetString() == "audio");
            Assert.Equal(("h264", "High", "yuv420p"), (picture.GetProperty("codec_name").GetString(), picture.GetProperty("profile").GetString(), picture.GetProperty("pix_fmt").GetString()));
            Assert.Equal(("aac", 2), (sound.GetProperty("codec_name").GetString(), sound.GetProperty("channels").GetInt32()));
            using BlockCache contents = new BlockCache(new MemoryStream(stream));
            Mp4Index index = (await Mp4Index.ReadAsync(contents, CancellationToken.None))!;
            Assert.True(index.IndexAtFront);
            Assert.Equal(1, index.MaxKeyframeGapSeconds!.Value, precision: 1);
        }

        [FfmpegFact]
        public async Task MakeAsync_Stream_IsAtMost1080p()
        {
            byte[] video = await TestVideos.Mp4Async("testsrc2=size=2560x1440:rate=5:duration=2", "-c:v", "libx264", "-preset", "ultrafast", "-qp", "0");

            MadeFiles made = await MadeFiles.MakeAsync(video, DerivedKind.Stream);

            JsonElement picture = await TestVideos.VideoStreamAsync(made.BytesOf(DerivedKind.Stream), ".mp4");
            Assert.Equal((1920, 1080), (picture.GetProperty("width").GetInt32(), picture.GetProperty("height").GetInt32()));
        }

        [FfmpegFact]
        public async Task MakeAsync_Stream_OfAnUprightVideoIsAtMost1080Wide()
        {
            byte[] video = await TestVideos.Mp4Async("testsrc2=size=1440x2560:rate=5:duration=2", "-c:v", "libx264", "-preset", "ultrafast", "-qp", "0");

            MadeFiles made = await MadeFiles.MakeAsync(video, DerivedKind.Stream);

            JsonElement picture = await TestVideos.VideoStreamAsync(made.BytesOf(DerivedKind.Stream), ".mp4");
            Assert.Equal((1080, 1920), (picture.GetProperty("width").GetInt32(), picture.GetProperty("height").GetInt32()));
        }

        [FfmpegFact]
        public async Task MakeAsync_Stream_OfAVideoThatStreamsWell_IsNotMade()
        {
            MadeFiles made = await MadeFiles.MakeAsync(await TestVideos.StreamingWellAsync(), DerivedKind.Stream);

            Assert.Empty(made.Kinds);
        }

        /// <summary>Of so few bits a second, the copy comes out larger.</summary>
        [FfmpegFact]
        public async Task MakeAsync_Stream_LargerThanTheVideo_IsNotKept()
        {
            byte[] video = await TestVideos.Mp4Async("testsrc2=size=320x240:rate=30:duration=4", "-c:v", "libx264", "-crf", "51", "-g", "300");

            MadeFiles made = await MadeFiles.MakeAsync(video, DerivedKind.Stream);

            Assert.Empty(made.Kinds);
        }

        [FfmpegFact]
        public async Task MakeAsync_ThumbnailAndPreview_MakeBothAndNoStream()
        {
            MadeFiles made = await MadeFiles.MakeAsync(await TestVideos.SparseKeyframesAsync(), DerivedKind.Thumbnail, DerivedKind.Preview);

            Assert.Equal([DerivedKind.Thumbnail, DerivedKind.Preview], made.Kinds);
        }

        [FfmpegFact]
        public async Task MakeAsync_NoVideo_MakesNothing()
        {
            MadeFiles made = await MadeFiles.MakeAsync(Encoding.UTF8.GetBytes("Not a video."), DerivedKind.Thumbnail, DerivedKind.Preview, DerivedKind.Stream);

            Assert.Empty(made.Kinds);
        }

        /// <summary>20 seconds of 160 by 120, black but from <paramref name="from"/> to <paramref name="to"/> seconds, white: a storyboard of 10 frames in a row.</summary>
        private static string Lit(double from, double to)
        {
            return string.Create(CultureInfo.InvariantCulture, $"color=c=black:size=160x120:rate=10:duration=20,drawbox=c=white:t=fill:enable='between(t,{from},{to})'");
        }

        /// <summary>Which frames of a storyboard of <see cref="Lit"/>'s are white.</summary>
        private static async Task<IEnumerable<int>> WhiteFramesAsync(byte[] storyboard)
        {
            List<int> white = [];
            for (int frame = 0; frame < 10; frame++)
            {
                if (await TestVideos.MeanGrayAsync(storyboard, string.Create(CultureInfo.InvariantCulture, $"160:120:{frame * 160}:0")) > 128)
                {
                    white.Add(frame);
                }
            }

            return white;
        }

        private static async Task<bool> StreamsWellAsync(byte[] video)
        {
            await using VideoSource source = (await VideoSource.OpenAsync(TestVideos.Ffmpeg, new MemoryStream(video)))!;
            return StreamsWell.Of(source.Probe);
        }
    }
}
