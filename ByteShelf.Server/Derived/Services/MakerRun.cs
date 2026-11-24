namespace ByteShelf.Server.Derived.Services
{
    /// <summary>How a run of a maker over some contents ended.</summary>
    internal abstract record MakerRun
    {
        private MakerRun()
        {
        }

        internal sealed record Made(IReadOnlyList<MadeInFolder> Files) : MakerRun;

        /// <param name="Reason">Why, without anything read from the contents.</param>
        internal sealed record Failed(string Reason) : MakerRun;

        /// <summary>The contents could not be opened: removed meanwhile, or not readable.</summary>
        internal sealed record ContentsNotOpened : MakerRun;
    }
}
