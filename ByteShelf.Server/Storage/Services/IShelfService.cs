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

        /// <summary>
        /// Replaces a shelf's name and quota. A null <paramref name="quotaBytes"/> removes the quota, so
        /// send the current one to keep it.
        /// </summary>
        /// <exception cref="Shared.Errors.NotFoundException">The shelf has been deleted meanwhile.</exception>
        /// <exception cref="Shared.Errors.ConflictException">The new name is taken.</exception>
        Task<Shelf> UpdateAsync(Shelf shelf, string name, long? quotaBytes);

        /// <summary>Deletes a shelf that holds nothing, the trash included.</summary>
        /// <exception cref="Shared.Errors.NotFoundException">The shelf has been deleted meanwhile.</exception>
        /// <exception cref="Shared.Errors.ConflictException">The shelf still holds something.</exception>
        Task DeleteAsync(Shelf shelf);
    }
}
