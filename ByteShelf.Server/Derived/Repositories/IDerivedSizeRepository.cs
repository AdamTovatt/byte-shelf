using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Derived.Repositories
{
    /// <summary>
    /// The bytes each derived file on disk takes (<c>derived_size</c>), which shelves' usage counts. Written only as the
    /// files are kept or removed (<see cref="Services.DerivedFiles"/>), and by maintenance bringing it in line with them.
    /// </summary>
    public interface IDerivedSizeRepository
    {
        /// <summary>The bytes the derived file of <paramref name="kind"/> takes; null when none is counted.</summary>
        Task<long?> GetAsync(string sha256, DerivedKind kind, IDbSession? session = null);

        /// <summary>Every row, by the contents and kind it is of.</summary>
        Task<IReadOnlyDictionary<(string Sha256, DerivedKind Kind), long>> GetAllAsync(IDbSession? session = null);

        Task SetAsync(string sha256, DerivedKind kind, long sizeBytes, IDbSession session);

        Task RemoveAsync(string sha256, DerivedKind kind, IDbSession session);

        /// <summary>Removes the rows of every kind derived from the contents.</summary>
        Task RemoveAsync(string sha256, IDbSession session);

        /// <summary>Removes every row: for a copy of the database that goes where the folder does not.</summary>
        Task RemoveAllAsync(IDbSession session);
    }
}
