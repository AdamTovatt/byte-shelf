using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Repositories;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>
    /// The server's own reader: <see cref="MetadataReader"/>, in the server's process, whose findings go to
    /// <c>object_metadata</c>. Contents another <see cref="MetadataReader.Version"/> read are read again.
    /// </summary>
    /// <remarks>
    /// A swap does not end a read under way: what is left of it is memory of a process that held the plaintext to
    /// store it, and what it finds is recorded only while a plain file holds the contents.
    /// </remarks>
    internal sealed class BuiltInContentReader : ContentReader
    {
        private readonly IObjectMetadataRepository _repository;
        private readonly ContentReadRunner _runner;

        public BuiltInContentReader(IObjectMetadataRepository repository, ContentReadRunner runner)
        {
            _repository = repository;
            _runner = runner;
        }

        public override bool MayRun => _runner.MayRun;

        public override Task<IReadOnlyList<string>> GetUnreadAsync(string? after, int limit)
        {
            return _repository.GetUnreadAsync(MetadataReader.Version, ReadMediaTypes.All, after, limit);
        }

        public override Task<ReadOutcome?> ReadAsync(string sha256, CancellationToken cancellationToken)
        {
            return _runner.ReadAsync(sha256, contents => Record(MetadataReader.Read(contents)), Record(ContentMetadata.None), cancellationToken);
        }

        private RecordReading Record(ContentMetadata found)
        {
            return (sha256, at, session) => _repository.RecordAsync(ObjectMetadata.Of(sha256, MetadataReader.Version, at, found), session);
        }
    }
}
