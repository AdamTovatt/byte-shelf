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
            ValidateSettings(name, quotaBytes);

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

        /// <inheritdoc />
        public async Task<Shelf> UpdateAsync(Shelf shelf, string name, long? quotaBytes)
        {
            ValidateSettings(name, quotaBytes);

            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                Shelf current = await GetForWriteAsync(shelf, session);
                Shelf updated = new Shelf
                {
                    Id = current.Id,
                    Name = name,
                    QuotaBytes = quotaBytes,
                    CreatedAt = current.CreatedAt,
                    UpdatedAt = _timeProvider.GetUtcNow(),
                };

                if (!await _shelfRepository.TryUpdateAsync(updated, session))
                {
                    throw new ConflictException($"A shelf called '{name}' already exists.");
                }

                await session.CommitAsync();
                return updated;
            }
        }

        /// <inheritdoc />
        public async Task DeleteAsync(Shelf shelf)
        {
            await using (IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync())
            {
                await GetForWriteAsync(shelf, session);

                // Checked inside the write transaction, so nothing can be stored between the check and
                // the delete.
                if (!await _shelfRepository.IsEmptyAsync(shelf.Id, session))
                {
                    throw new ConflictException(
                        $"The shelf '{shelf.Name}' still holds files, directories, uploads or trash; a shelf is deleted only once it is empty. Empty its trash to remove what is in it right away.");
                }

                DateTimeOffset now = _timeProvider.GetUtcNow();
                await _directoryRepository.DeleteRootAsync(shelf.Id, now, session);
                await _shelfRepository.DeleteAsync(shelf.Id, now, session);
                await session.CommitAsync();
            }
        }

        /// <summary>
        /// The shelf as it is inside the caller's write transaction. It was looked up before the
        /// transaction began, and a delete may have committed since; writing on would log a change to a
        /// shelf that is gone.
        /// </summary>
        private async Task<Shelf> GetForWriteAsync(Shelf shelf, IDbSession session)
        {
            return await _shelfRepository.GetByIdAsync(shelf.Id, session)
                ?? throw new NotFoundException($"There is no shelf '{shelf.Name}'.");
        }

        private static void ValidateSettings(string name, long? quotaBytes)
        {
            ShelfName.Validate(name);
            if (quotaBytes < 0)
            {
                throw new ArgumentException("A quota cannot be negative.");
            }
        }
    }
}
