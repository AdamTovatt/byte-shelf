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

    /// <summary>As <see cref="LinuxFactAttribute"/>.</summary>
    internal sealed class LinuxTheoryAttribute : TheoryAttribute
    {
        public LinuxTheoryAttribute()
        {
            Skip = LinuxOnly.SkipReason;
        }
    }

    internal static class LinuxOnly
    {
        public static string? SkipReason => OperatingSystem.IsLinux() ? null : "It is so on Linux alone.";
    }
}
