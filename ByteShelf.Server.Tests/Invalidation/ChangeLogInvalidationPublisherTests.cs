using System.Threading.Channels;
using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using ByteShelf.Server.Invalidation.Models;
using ByteShelf.Server.Invalidation.Services;
using ByteShelf.Server.Tests.TestUtilities;
using EasyReasy.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ByteShelf.Server.Tests.Invalidation
{
    /// <summary>How the publisher follows the change log: batches, duplicates, startup and failures.</summary>
    public sealed class ChangeLogInvalidationPublisherTests : IDisposable
    {
        private readonly FakeChangeLog _changeLog = new FakeChangeLog();
        private readonly RecordingBroadcaster _broadcaster = new RecordingBroadcaster();
        private readonly FakeTimeProvider _clock = new FakeTimeProvider(TestInstants.Now);
        private readonly ServiceProvider _services;
        private readonly ChangeLogInvalidationPublisher _publisher;

        public ChangeLogInvalidationPublisherTests()
        {
            _services = new ServiceCollection()
                .AddSingleton<IChangeLogRepository>(_changeLog)
                .BuildServiceProvider();
            _publisher = new ChangeLogInvalidationPublisher(
                _services.GetRequiredService<IServiceScopeFactory>(),
                _broadcaster,
                _clock,
                NullLogger<ChangeLogInvalidationPublisher>.Instance);
        }

        [Fact]
        public async Task PublishAfterAsync_MoreThanOneBatch_PublishesEveryEntryAndAnswersTheLast()
        {
            List<Guid> shelves = Enumerable.Range(0, ChangeLogInvalidationPublisher.BatchSize + 2).Select(_ => Guid.NewGuid()).ToList();
            foreach (Guid shelf in shelves)
            {
                _changeLog.AddKey(shelf);
            }

            long published = await _publisher.PublishAfterAsync(0);

            Assert.Equal(shelves.Count, published);
            Assert.Equal(shelves.Select(shelf => shelf.ToString("D")), _broadcaster.Published.Select(invalidation => invalidation.Metadata));
            Assert.All(_changeLog.Limits, limit => Assert.Equal(ChangeLogInvalidationPublisher.BatchSize, limit));
        }

        [Fact]
        public async Task PublishAfterAsync_SameViewStaleTwice_PublishesItOnce()
        {
            Guid shelf = Guid.NewGuid();
            _changeLog.AddKey(shelf);
            _changeLog.AddKey(shelf);

            await _publisher.PublishAfterAsync(0);

            Assert.Equal(new[] { new InvalidationEvent(InvalidationResources.ShelfAccess, shelf.ToString("D")) }, _broadcaster.Published);
        }

        [Fact]
        public async Task PublishAfterAsync_NothingNew_PublishesNothing()
        {
            _changeLog.AddKey(Guid.NewGuid());

            long published = await _publisher.PublishAfterAsync(1);

            Assert.Equal(1, published);
            Assert.Empty(_broadcaster.Published);
        }

        [Fact]
        public async Task ExecuteAsync_ChangesFromBeforeStart_AreNotPublished()
        {
            _changeLog.AddKey(Guid.NewGuid());
            Guid later = Guid.NewGuid();

            await _publisher.StartAsync(CancellationToken.None);
            await _changeLog.LatestRead;
            _changeLog.AddKey(later);
            await _clock.AdvanceUntilAsync(_broadcaster.WaitForAsync(1), ChangeLogInvalidationPublisher.PollInterval);
            await _publisher.StopAsync(CancellationToken.None);

            Assert.Equal(later.ToString("D"), _broadcaster.Published[0].Metadata);
        }

        [Fact]
        public async Task ExecuteAsync_ReadFails_PublishesOnTheNextPoll()
        {
            Guid shelf = Guid.NewGuid();
            _changeLog.FailNextRead = true;

            await _publisher.StartAsync(CancellationToken.None);
            await _changeLog.LatestRead;
            _changeLog.AddKey(shelf);
            await _clock.AdvanceUntilAsync(_broadcaster.WaitForAsync(1), ChangeLogInvalidationPublisher.PollInterval);
            await _publisher.StopAsync(CancellationToken.None);

            Assert.False(_changeLog.FailNextRead);
            Assert.Equal(shelf.ToString("D"), _broadcaster.Published[0].Metadata);
        }

        public void Dispose()
        {
            _publisher.Dispose();
            _services.Dispose();
        }

        /// <summary>An in-memory log; only the reads the publisher makes are supported.</summary>
        private sealed class FakeChangeLog : IChangeLogRepository
        {
            private readonly object _gate = new object();
            private readonly List<ChangeLogEntry> _entries = new List<ChangeLogEntry>();
            private readonly List<int> _limits = new List<int>();
            private readonly TaskCompletionSource _latestRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            private bool _failNextRead;

            public bool FailNextRead
            {
                get { lock (_gate) { return _failNextRead; } }
                set { lock (_gate) { _failNextRead = value; } }
            }

            public IReadOnlyList<int> Limits
            {
                get { lock (_gate) { return _limits.ToList(); } }
            }

            public void AddKey(Guid shelfId)
            {
                lock (_gate)
                {
                    _entries.Add(new ChangeLogEntry
                    {
                        Sequence = _entries.Count + 1,
                        EntityType = ChangeEntityType.ShelfKey,
                        EntityId = Guid.NewGuid(),
                        Action = ChangeAction.Added,
                        Payload = ChangePayload.Serialize(ChangeStates.Key(shelfId)),
                        CreatedAt = TestInstants.Now,
                    });
                }
            }

            public Task<IReadOnlyList<ChangeLogEntry>> GetAfterAsync(long afterSequence, int limit, IDbSession? session = null)
            {
                lock (_gate)
                {
                    if (_failNextRead)
                    {
                        _failNextRead = false;
                        throw new InvalidOperationException("The database is busy (test).");
                    }

                    _limits.Add(limit);
                    return Task.FromResult<IReadOnlyList<ChangeLogEntry>>(
                        _entries.Where(entry => entry.Sequence > afterSequence).Take(limit).ToList());
                }
            }

            /// <summary>Completes once the publisher has read where the log stood at its start.</summary>
            public Task LatestRead => _latestRead.Task.WaitAsync(TimeSpan.FromSeconds(10));

            public Task<long> GetLatestSequenceAsync(IDbSession? session = null)
            {
                lock (_gate)
                {
                    _latestRead.TrySetResult();
                    return Task.FromResult((long)_entries.Count);
                }
            }

            public Task AppendAsync<TState>(string entityType, Guid entityId, string action, TState? state, DateTimeOffset at, IDbSession session)
                where TState : class
            {
                throw new NotSupportedException();
            }

            public Task<int> DeleteAndLogAsync(string entityType, string deleteReturningIds, object parameters, DateTimeOffset at, IDbSession session)
            {
                throw new NotSupportedException();
            }

            public Task<IReadOnlyList<ChangeLogEntry>> GetLatestStatesAfterAsync(long afterSequence, int limit, IDbSession? session = null)
            {
                throw new NotSupportedException();
            }

            public Task<int> CompactAsync(DateTimeOffset before, IDbSession session)
            {
                throw new NotSupportedException();
            }

            public Task<long> GetPrunedThroughAsync(IDbSession? session = null)
            {
                throw new NotSupportedException();
            }

            public Task<ChangeLogEntry?> GetBySequenceAsync(long sequence, IDbSession? session = null)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class RecordingBroadcaster : IInvalidationBroadcaster
        {
            private readonly object _gate = new object();
            private readonly List<InvalidationEvent> _published = new List<InvalidationEvent>();
            private readonly Channel<int> _counts = Channel.CreateUnbounded<int>();

            public IReadOnlyList<InvalidationEvent> Published
            {
                get { lock (_gate) { return _published.ToList(); } }
            }

            public void Publish(InvalidationEvent invalidationEvent)
            {
                lock (_gate)
                {
                    _published.Add(invalidationEvent);
                    _counts.Writer.TryWrite(_published.Count);
                }
            }

            /// <summary>Completes once <paramref name="count"/> events have been published.</summary>
            public async Task WaitForAsync(int count)
            {
                while (Published.Count < count)
                {
                    await _counts.Reader.ReadAsync();
                }
            }

            public IInvalidationSubscription? Subscribe(string callerKey)
            {
                throw new NotSupportedException();
            }
        }
    }
}
