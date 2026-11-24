using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>Which derived files there are of files, for listings to say. Nothing is made for it.</summary>
    public interface IDerivedListing
    {
        /// <summary>The kinds there are of <paramref name="file"/>, made at the versions installed plugins make them, with those; null for none.</summary>
        IReadOnlyDictionary<DerivedKind, string>? Of(FileRecord file);
    }
}
