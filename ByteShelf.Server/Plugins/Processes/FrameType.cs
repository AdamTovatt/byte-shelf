namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>What a frame between the server and a plugin's process says.</summary>
    internal enum FrameType : byte
    {
        /// <summary>From the server: do this (<see cref="RunRequest"/>).</summary>
        Run = 1,

        /// <summary>From the server: the bytes last asked for.</summary>
        Bytes = 2,

        /// <summary>From the process: the plugin is loaded.</summary>
        Ready = 3,

        /// <summary>From the process: it begins what was asked; how it ends from here on is the contents' doing.</summary>
        Started = 4,

        /// <summary>From the process: some of the contents, by an offset and a count.</summary>
        Read = 5,

        /// <summary>From the process: the answer.</summary>
        Answer = 6,

        /// <summary>From the process: the plugin threw; the exception's type.</summary>
        Failed = 7,

        /// <summary>From the process: the plugin cannot do it, whatever the contents; why.</summary>
        Unavailable = 8,
    }
}
