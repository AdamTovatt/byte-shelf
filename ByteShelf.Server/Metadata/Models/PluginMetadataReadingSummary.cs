namespace ByteShelf.Server.Metadata.Models
{
    /// <summary>What one pass of <c>metadata.plugin_reading</c> did, all plugins' readers together.</summary>
    /// <param name="OnReplica">The server follows a primary, whose rows reach it through the change log, so it read nothing.</param>
    /// <param name="Read">Contents read and recorded, those that failed included; ones two readers read count twice.</param>
    /// <param name="Failed">Of those read, how many their reader threw on or took too long over, recorded as saying nothing.</param>
    public sealed record PluginMetadataReadingSummary(bool OnReplica, int Read, int Failed);
}
