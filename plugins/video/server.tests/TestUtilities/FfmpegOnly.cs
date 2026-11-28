namespace ByteShelf.Plugins.Video.Tests.TestUtilities
{
    /// <summary>Why a test that runs the plugin's ffmpeg, which it has for Linux x64 and arm64 alone, is skipped elsewhere.</summary>
    internal static class FfmpegOnly
    {
        public static string? SkipReason => Ffmpeg.RuntimeOfThisMachine() == null ? "The plugin has no ffmpeg for this platform." : null;
    }
}
