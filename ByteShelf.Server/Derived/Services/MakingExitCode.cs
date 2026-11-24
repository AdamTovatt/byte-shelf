namespace ByteShelf.Server.Derived.Services
{
    /// <summary>How a child process ended, where it says so itself.</summary>
    internal enum MakingExitCode
    {
        /// <summary>The maker answered.</summary>
        Made = 0,

        /// <summary>The maker threw.</summary>
        Failed = 1,

        /// <summary>The process was not started as a server starts it.</summary>
        WrongArguments = 2,

        /// <summary>The contents could not be opened.</summary>
        ContentsNotOpened = 3,

        /// <summary>The plugin could not be loaded, or is another version than the server's.</summary>
        PluginUnavailable = 4,

        /// <summary>What was made, or that nothing was, could not be written.</summary>
        NotKept = 5,
    }
}
