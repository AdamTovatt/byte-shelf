using ByteShelf.Plugins.Video.Tests.TestUtilities;

namespace ByteShelf.Plugins.Video.Tests
{
    public sealed class FfmpegTests
    {
        /// <summary>The maker and the reader are made with it, so the plugin says it cannot run, which admins see.</summary>
        [Fact]
        public void Locate_APlatformItHasNoneFor_IsNotSupported()
        {
            Assert.Throws<PlatformNotSupportedException>(() => Ffmpeg.Locate(runtime: null, AppContext.BaseDirectory));
        }

        [Fact]
        public void Locate_AFolderWithoutThem_IsNotFound()
        {
            DirectoryInfo empty = Directory.CreateTempSubdirectory("video-tests-");
            try
            {
                Assert.Throws<FileNotFoundException>(() => Ffmpeg.Locate("linux-x64", empty.FullName));
            }
            finally
            {
                empty.Delete();
            }
        }

        [FfmpegFact]
        public void Locate_ThisMachine_IsTheBuildsBesideTheAssembly()
        {
            Ffmpeg ffmpeg = Ffmpeg.Locate();

            string folder = Path.Combine(AppContext.BaseDirectory, "ffmpeg", Ffmpeg.RuntimeOfThisMachine()!);
            Assert.Equal((Path.Combine(folder, "ffmpeg"), Path.Combine(folder, "ffprobe")), (ffmpeg.FfmpegPath, ffmpeg.FfprobePath));
        }

        /// <summary>The zip carries ffmpeg's licence and which build it is.</summary>
        [FfmpegFact]
        public void Locate_ThisMachine_HasTheLicenceAndWhatBuildItIs()
        {
            string folder = Path.GetDirectoryName(Ffmpeg.Locate().FfmpegPath)!;

            Assert.Contains("GNU GENERAL PUBLIC LICENSE", File.ReadAllText(Path.Combine(folder, "LICENSE.txt")));
            Assert.Contains("SHA-256", File.ReadAllText(Path.Combine(folder, "BUILD.txt")));
        }

        [FfmpegFact]
        public async Task RunFfmpegAsync_AnError_Throws()
        {
            await Assert.ThrowsAsync<FfmpegFailedException>(() => TestVideos.Ffmpeg.RunFfmpegAsync(["-i", "/nothing/at/all.mp4", "-f", "null", "-"]));
        }

        /// <summary>An input of another protocol, or none, as a file of the machine's.</summary>
        [FfmpegFact]
        public async Task Input_AnyButHttp_IsRefused()
        {
            string file = Path.GetTempFileName();
            try
            {
                await File.WriteAllBytesAsync(file, await TestVideos.StreamingWellAsync());

                await Assert.ThrowsAsync<FfmpegFailedException>(() => TestVideos.Ffmpeg.RunFfprobeAsync([.. Ffmpeg.Input("file:" + file, RangeServer.SeekingRequestBytes)]));
                await Assert.ThrowsAsync<FfmpegFailedException>(() => TestVideos.Ffmpeg.RunFfprobeAsync([.. Ffmpeg.Input(file, RangeServer.SeekingRequestBytes)]));
            }
            finally
            {
                File.Delete(file);
            }
        }

        /// <summary>A video's frames to standard output, which no one reads.</summary>
        [FfmpegFact]
        public async Task RunFfmpegAsync_WhatItPrints_IsThrownAway()
        {
            await TestVideos.Ffmpeg.RunFfmpegAsync(["-f", "lavfi", "-i", "testsrc2=size=320x240:rate=30:duration=2", "-f", "rawvideo", "-"]);
        }

        [FfmpegFact]
        public async Task RunFfprobeAsync_MoreThanItsMost_Throws()
        {
            string[] arguments = ["-f", "lavfi", "-print_format", "json", "-show_streams", "testsrc2=size=320x240:rate=30:duration=1"];
            int printed = (await TestVideos.Ffmpeg.RunFfprobeAsync(arguments)).Length;

            Assert.Equal(printed, (await TestVideos.Ffmpeg.RunFfprobeAsync(arguments, maxAnswerChars: printed)).Length);
            await Assert.ThrowsAsync<FfmpegFailedException>(() => TestVideos.Ffmpeg.RunFfprobeAsync(arguments, maxAnswerChars: printed - 1));
        }
    }
}
