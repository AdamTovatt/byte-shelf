namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>A plugin this server will not load; the message says why, as a sentence.</summary>
    public sealed class PluginRefusedException : Exception
    {
        public PluginRefusedException(string message)
            : base(message)
        {
        }

        public PluginRefusedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
