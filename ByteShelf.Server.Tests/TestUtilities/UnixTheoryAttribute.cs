namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>A theory of what holds on Unix only, reported as skipped elsewhere rather than passing there unchecked.</summary>
    public sealed class UnixTheoryAttribute : TheoryAttribute
    {
        public UnixTheoryAttribute()
        {
            if (OperatingSystem.IsWindows())
            {
                Skip = "This is Unix only.";
            }
        }
    }
}
