using ByteShelf.Server.Shared.Errors;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Storage.Services
{
    /// <inheritdoc />
    public class ShelfService : IShelfService
    {
        private readonly IDbSessionFactory _sessionFactory;
        private readonly IShelfRepository _shelfRepository;
        private readonly IDirectoryRepository _directoryRepository;
        private readonly TimeProvider _timeProvider;

        public ShelfService(
            IDbSessionFactory sessionFactory,
            IShelfRepository shelfRepository,
            IDirectoryRepository directoryRepository,
            TimeProvider timeProvider)
        {
            _sessionFactory = sessionFactory;
            _shelfRepository = shelfRepository;
            _directoryRepository = directoryRepository;
            _timeProvider = timeProvider;
        }

        /// <inheritdoc />
        public async Task<Shelf> GetByNameAsync(string name)
        {
            return await _shelfRepository.GetByNameAsync(name)
                ?? throw new NotFoundException($"There is no shelf '{name}'.");
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ShelfInfo>> GetAllAsync()
        {
            await using (IDbSession session = await _sessionFactory.CreateSessionAsync())
            {
                IReadOnlyList<Shelf> shelves = await _shelfRepository.GetAllAsync(session);
                List<ShelfInfo> result = new List<ShelfInfo>(shelves.Count);

                foreach (Shelf shelf in shelves)
                {
                    result.Add(StorageContracts.ToShelfInfo(shelf, await _shelfRepository.GetUsageAsync(shelf.Id, session)));
                }

                return result;
            }
        }

        /// <inheritdoc />
        public async Task<ShelfInfo> GetInfoAsync(Shelf shelf)
        {
            return StorageContracts.ToShelfInfo(shelf, await _shelfRepository.GetUsageAsync(shelf.Id));
        }

        /// <inheritdoc />
        public async Task<Shelf> CreateAsync(string name, long? quotaBytes)
        {
            ShelfName.Validate(name);
            if (quotaBytes < 0)
            {
                throw new ArgumentException("A quota cannot be negative.");
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            Shelf shelf = new Shelf
            {
                Id = Guid.NewGuid(),
                Name = name,
                QuotaBytes = quotaBytes,
                CreatedAt = now,
                UpdatedAt = now,
            };

            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                if (!await _shelfRepository.TryInsertAsync(shelf, session))
                {
                    throw new ConflictException($"A shelf called '{name}' already exists.");
                }

                await _directoryRepository.InsertAsync(
                    new DirectoryRecord
                    {
                        Id = Guid.NewGuid(),
                        ShelfId = shelf.Id,
                        ParentId = null,
                        Name = string.Empty,
                        CreatedAt = now,
                        UpdatedAt = now,
                    },
                    session);

                await session.CommitAsync();
                return shelf;
            }
        }
    }
}
