namespace ByteShelf.Server.Plugins.Models
{
    /// <summary>What a plugin registered to be done in its process.</summary>
    /// <param name="Reader">Its reader of files' contents; null when it has none.</param>
    /// <param name="Maker">Its maker of derived files; null when it has none.</param>
    public sealed record PluginRegistrations(PluginReaderRegistration? Reader, PluginMakerRegistration? Maker);
}
