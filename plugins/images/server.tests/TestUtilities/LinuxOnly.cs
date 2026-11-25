namespace ByteShelf.Plugins.Images.Tests.TestUtilities
{
    /// <summary>Why a test of what is so on Linux alone is skipped elsewhere.</summary>
    internal static class LinuxOnly
    {
        public static string? SkipReason => OperatingSystem.IsLinux() ? null : "It is so on Linux alone.";
    }
}
