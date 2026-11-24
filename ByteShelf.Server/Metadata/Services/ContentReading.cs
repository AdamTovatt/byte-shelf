using EasyReasy.Database;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>Runs one reader over every object that a live, plain file of its types holds and it has not read, whenever it was stored.</summary>
    internal sealed class ContentReading
    {
        /// <summary>How many objects one query lists to read.</summary>
        internal const int BatchSize = 100;

        private readonly IDbSessionFactory _sessionFactory;
        private readonly TimeProvider _timeProvider;

        public ContentReading(IDbSessionFactory sessionFactory, TimeProvider timeProvider)
        {
            _sessionFactory = sessionFactory;
            _timeProvider = timeProvider;
        }

        /// <summary>
        /// Reads everything <paramref name="reader"/> has yet to read, until it may not run any more; answers how
        /// much it read, and how much of that failed.
        /// </summary>
        public async Task<(int Read, int Failed)> RunAsync(ContentReader reader, CancellationToken cancellationToken)
        {
            int read = 0;
            int failed = 0;

            // Listed after the last one tried rather than from the start, so contents that vanish
            // before they are read are passed over instead of listed again.
            string? after = null;
            while (await reader.GetUnreadAsync(after, BatchSize) is { Count: > 0 } unread)
            {
                foreach (string sha256 in unread)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!reader.MayRun)
                    {
                        return (read, failed);
                    }

                    after = sha256;
                    if (await reader.ReadAsync(sha256, cancellationToken) is not { } outcome)
                    {
                        continue;
                    }

                    await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
                    {
                        // False when another pass recorded them meanwhile, or no live plain file holds them any more.
                        if (await outcome.Record(sha256, _timeProvider.GetUtcNow(), session))
                        {
                            read++;
                            failed += outcome.Failed ? 1 : 0;
                        }

                        await session.CommitAsync();
                    }
                }
            }

            return (read, failed);
        }
    }
}
