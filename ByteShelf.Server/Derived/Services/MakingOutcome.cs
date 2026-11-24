namespace ByteShelf.Server.Derived.Services
{
    /// <summary>How making something ended.</summary>
    internal enum MakingOutcome
    {
        /// <summary>What was made, or that making it failed, is kept.</summary>
        Kept,

        /// <summary>It was there already, or there are no contents to make it from.</summary>
        NothingToMake,

        /// <summary>It was stopped, as for contents being removed or a server shutting down.</summary>
        Stopped,
    }
}
