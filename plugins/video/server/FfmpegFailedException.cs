namespace ByteShelf.Plugins.Video
{
    /// <summary>ffmpeg or ffprobe did not do what it was asked, which says nothing of the contents.</summary>
    public sealed class FfmpegFailedException : Exception
    {
        internal FfmpegFailedException(string tool, string what)
            : base($"{tool} {what}.")
        {
        }
    }
}
