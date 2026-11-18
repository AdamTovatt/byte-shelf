using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Repositories;
using ByteShelf.Server.Replication.Services;
using ByteShelf.Server.Storage.Services;
using EasyReasy.Database;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>
    /// One pass of the job <c>metadata.reading</c>: reads every object a live, plain file of a type in
    /// <see cref="ReadMediaTypes"/> holds and nothing has been read from yet, whenever it was stored, and
    /// drops what was read from objects no file holds any more. Only a primary reads; a replica takes the
    /// rows from its primary's change log, so it does nothing.
    /// </summary>
    public sealed class MetadataReading
    {
        /// <summary>How many objects one query lists to read.</summary>
        internal const int BatchSize = 100;

        /// <summary>The reads a stream gets buffered by: the object store's own streams read unbuffered, and the parsers read a few bytes at a time.</summary>
        private const int ReadBufferBytes = 64 * 1024;

        private readonly IObjectMetadataRepository _repository;
        private readonly IDbSessionFactory _sessionFactory;
        private readonly ObjectStore _objectStore;
        private readonly ReplicationState _replicationState;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<MetadataReading> _logger;

        public MetadataReading(
            IObjectMetadataRepository repository,
            IDbSessionFactory sessionFactory,
            ObjectStore objectStore,
            ReplicationState replicationState,
            TimeProvider timeProvider,
            ILogger<MetadataReading> logger)
        {
            _repository = repository;
            _sessionFactory = sessionFactory;
            _objectStore = objectStore;
            _replicationState = replicationState;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        public async Task<MetadataReadingSummary> RunAsync(CancellationToken cancellationToken)
        {
            if (!await _replicationState.AllowsWritesAsync())
            {
                return new MetadataReadingSummary(OnReplica: true, Read: 0, Damaged: 0, Removed: 0);
            }

            int removed;
            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                removed = await _repository.DeleteUnreferencedAsync(_timeProvider.GetUtcNow(), session);
                await session.CommitAsync();
            }

            // Listed after the last one tried rather than from the start, so contents that vanish
            // before they are read are passed over instead of listed again.
            int read = 0;
            int damaged = 0;
            string? after = null;
            while (await _repository.GetUnreadAsync(after, BatchSize) is { Count: > 0 } unread)
            {
                foreach (string sha256 in unread)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    after = sha256;

                    (ContentMetadata Metadata, bool Damaged)? found = Read(sha256);
                    if (found is not { } result)
                    {
                        continue;
                    }

                    await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
                    {
                        // False when another pass recorded them meanwhile, which read the same bytes.
                        if (await _repository.InsertAsync(ObjectMetadata.Of(sha256, _timeProvider.GetUtcNow(), result.Metadata), session))
                        {
                            read++;
                            damaged += result.Damaged ? 1 : 0;
                        }

                        await session.CommitAsync();
                    }
                }
            }

            return new MetadataReadingSummary(OnReplica: false, read, damaged, removed);
        }

        /// <summary>
        /// What the object says, or that it is damaged, recorded as saying nothing so it is not read again;
        /// null when it is gone, removed with the last file holding it after it was listed, which leaves
        /// nothing to record.
        /// </summary>
        private (ContentMetadata Metadata, bool Damaged)? Read(string sha256)
        {
            Stream contents;
            try
            {
                contents = new BufferedStream(_objectStore.OpenRead(sha256), ReadBufferBytes);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                return null;
            }

            using (contents)
            {
                try
                {
                    return (MetadataReader.Read(contents), Damaged: false);
                }
                catch (Exception exception)
                {
                    // The parsers throw whatever damaged contents lead them to, truncated ones included as
                    // IOException subtypes (BufferBoundsException, EndOfStreamException), so I/O errors are
                    // not let through to be retried: every damaged file would then fail every pass. Contents
                    // are written once and never change, so reading them again would fail the same way;
                    // a passing disk error on a local file is too rare to tell apart.
                    _logger.LogInformation(exception, "The contents {Sha256} could not be read for metadata; they are recorded as carrying none.", sha256);
                    return (ContentMetadata.None, Damaged: true);
                }
            }
        }
    }
}
