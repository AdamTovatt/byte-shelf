namespace ByteShelf.Plugins.Images.Tests.TestUtilities
{
    /// <summary>A test of what is so on Linux alone.</summary>
    internal sealed class LinuxFactAttribute : FactAttribute
    {
        public LinuxFactAttribute()
        {
            Skip = LinuxOnly.SkipReason;
        }
    }
}
