namespace ByteShelf.Server.Plugins.Processes
{
    /// <summary>A plugin's process that read contents is still there though it was killed, or what it wrote is.</summary>
    internal sealed class PluginNotStoppedException : Exception
    {
        public PluginNotStoppedException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}
