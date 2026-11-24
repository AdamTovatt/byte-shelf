namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>A theory of what holds on Linux only, reported as skipped elsewhere rather than passing there unchecked.</summary>
    public sealed class LinuxTheoryAttribute : TheoryAttribute
    {
        public LinuxTheoryAttribute()
        {
            if (!OperatingSystem.IsLinux())
            {
                Skip = "This is Linux only.";
            }
        }
    }
}
