namespace ByteShelf.Server.Plugins.Models
{
    /// <summary>A part of a plugin that runs in its process.</summary>
    public enum PluginPart
    {
        /// <summary>Its reader of files' contents.</summary>
        Reader,

        /// <summary>Its maker of derived files.</summary>
        Maker,
    }
}
