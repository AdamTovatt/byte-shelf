namespace ByteShelf.Plugins.Video.Tests.TestUtilities
{
    /// <summary>A test that runs the plugin's ffmpeg (<see cref="FfmpegOnly"/>).</summary>
    internal sealed class FfmpegFactAttribute : FactAttribute
    {
        public FfmpegFactAttribute()
        {
            Skip = FfmpegOnly.SkipReason;
        }
    }
}
