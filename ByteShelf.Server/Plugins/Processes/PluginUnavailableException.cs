namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>
    /// A plugin could not run, which says nothing of the contents. The message ends a sentence about the plugin or its
    /// process: a clause in lower case, with no full stop.
    /// </summary>
    public sealed class PluginUnavailableException : Exception
    {
        public PluginUnavailableException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }

        /// <summary>What is at the plugin's location now is not what the server loaded.</summary>
        public static PluginUnavailableException OtherVersion()
        {
            return new PluginUnavailableException("it is another version than when the server started");
        }
    }
}
