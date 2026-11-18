namespace ByteShelf.Server.Metadata.Models
{
    /// <summary>What one pass of <c>metadata.reading</c> did.</summary>
    /// <param name="OnReplica">The server follows a primary, whose rows reach it through the change log, so it read nothing.</param>
    /// <param name="Read">Contents read and recorded, those found damaged included.</param>
    /// <param name="Damaged">Of those read, how many the libraries could not parse, recorded as saying nothing.</param>
    /// <param name="Removed">Rows dropped because no file holds their contents any more.</param>
    public sealed record MetadataReadingSummary(bool OnReplica, int Read, int Damaged, int Removed);
}
