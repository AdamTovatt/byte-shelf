namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>What came of a plugin's reader reading some contents.</summary>
    public abstract record PluginReading
    {
        private PluginReading()
        {
        }

        /// <param name="Json">The document; null for nothing.</param>
        public sealed record Found(string? Json) : PluginReading;

        /// <summary>It threw, crashed or took too long.</summary>
        public sealed record Failed : PluginReading;

        /// <summary>It read nothing, as the contents are gone, or no plain file holds them any more.</summary>
        public sealed record NotRead : PluginReading;
    }
}
