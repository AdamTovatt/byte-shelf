namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>A test of Unix file modes, reported as skipped on Windows rather than passing there unchecked.</summary>
    public sealed class UnixFactAttribute : FactAttribute
    {
        public UnixFactAttribute()
        {
            if (OperatingSystem.IsWindows())
            {
                Skip = "File modes are Unix only.";
            }
        }
    }
}
