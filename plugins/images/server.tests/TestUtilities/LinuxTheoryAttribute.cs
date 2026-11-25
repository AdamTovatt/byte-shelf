namespace ByteShelf.Plugins.Images.Tests.TestUtilities
{
    /// <summary>As <see cref="LinuxFactAttribute"/>.</summary>
    internal sealed class LinuxTheoryAttribute : TheoryAttribute
    {
        public LinuxTheoryAttribute()
        {
            Skip = LinuxOnly.SkipReason;
        }
    }
}
