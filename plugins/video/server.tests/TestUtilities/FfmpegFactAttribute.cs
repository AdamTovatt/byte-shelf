namespace ByteShelf.Plugins.Video.Tests.TestUtilities
{
    /// <summary>A test that runs the plugin's ffmpeg, which it has for Linux x64 and arm64 alone.</summary>
    internal sealed class FfmpegFactAttribute : FactAttribute
    {
        public FfmpegFactAttribute()
        {
            Skip = SkipReason;
        }

        public static string? SkipReason => Ffmpeg.RuntimeOfThisMachine() == null ? "The plugin has no ffmpeg for this platform." : null;
    }

    /// <summary>As <see cref="FfmpegFactAttribute"/>.</summary>
    internal sealed class FfmpegTheoryAttribute : TheoryAttribute
    {
        public FfmpegTheoryAttribute()
        {
            Skip = FfmpegFactAttribute.SkipReason;
        }
    }
}
