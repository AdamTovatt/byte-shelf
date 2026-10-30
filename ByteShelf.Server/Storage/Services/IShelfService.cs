using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Services
{
    public interface IShelfService
    {
        /// <summary>The shelf called <paramref name="name"/>.</summary>
        /// <exception cref="Shared.Errors.NotFoundException">There is none.</exception>
        Task<Shelf> GetByNameAsync(string name);

        /// <summary>Every shelf with its usage, by name.</summary>
        Task<IReadOnlyList<ShelfInfo>> GetAllAsync();

        /// <summary>A shelf with its usage.</summary>
        Task<ShelfInfo> GetInfoAsync(Shelf shelf);

        /// <summary>Creates a shelf with its root directory.</summary>
        /// <exception cref="Shared.Errors.ConflictException">The name is taken.</exception>
        Task<Shelf> CreateAsync(string name, long? quotaBytes);
    }
}
