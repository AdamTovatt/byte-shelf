using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Repositories;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>
    /// The server's own reader: <see cref="MetadataReader"/>, whose findings go to <c>object_metadata</c>.
    /// It has no version, as its table has none: contents with a row are read.
    /// </summary>
    internal sealed class BuiltInContentReader : ContentReader
    {
        private readonly IObjectMetadataRepository _repository;

        public BuiltInContentReader(IObjectMetadataRepository repository)
        {
            _repository = repository;
        }

        // The colon is in no plugin's id, so it is never a plugin's reader's.
        public override string Id => "byteshelf:own";

        public override RecordReading Nothing => Record(ContentMetadata.None);

        public override Task<IReadOnlyList<string>> GetUnreadAsync(string? after, int limit)
        {
            return _repository.GetUnreadAsync(ReadMediaTypes.All, after, limit);
        }

        public override Task<RecordReading> ReadAsync(Stream contents, CancellationToken cancellationToken)
        {
            return Task.FromResult(Record(MetadataReader.Read(contents)));
        }

        private RecordReading Record(ContentMetadata found)
        {
            return (sha256, at, session) => _repository.RecordAsync(ObjectMetadata.Of(sha256, at, found), session);
        }
    }
}
