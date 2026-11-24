namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>A test of what holds on Linux only, reported as skipped elsewhere rather than passing there unchecked.</summary>
    public sealed class LinuxFactAttribute : FactAttribute
    {
        public LinuxFactAttribute()
        {
            if (!OperatingSystem.IsLinux())
            {
                Skip = "This is Linux only.";
            }
        }
    }
}
