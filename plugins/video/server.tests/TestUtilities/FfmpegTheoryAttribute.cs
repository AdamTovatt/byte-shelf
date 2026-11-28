namespace ByteShelf.Plugins.Video.Tests.TestUtilities
{
    /// <summary>A theory that runs the plugin's ffmpeg (<see cref="FfmpegOnly"/>).</summary>
    internal sealed class FfmpegTheoryAttribute : TheoryAttribute
    {
        public FfmpegTheoryAttribute()
        {
            Skip = FfmpegOnly.SkipReason;
        }
    }
}
