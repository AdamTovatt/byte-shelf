using ByteShelf.Server.Metadata.Repositories;
using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>
    /// Files as listings give them: with the values of the fields plugins declare for listings, read in one query,
    /// and the derived files there are of them.
    /// </summary>
    public class ListedFiles
    {
        private readonly IPluginFieldValueRepository _fieldValues;
        private readonly IDerivedListing _derived;

        public ListedFiles(IPluginFieldValueRepository fieldValues, IDerivedListing derived)
        {
            _fieldValues = fieldValues;
            _derived = derived;
        }

        /// <summary>One file's own record: as listings give it, but for the fields.</summary>
        public ShelfFile RecordOf(FileRecord file, string directoryPath)
        {
            return StorageContracts.ToShelfFile(file, directoryPath, derived: _derived.Of(file));
        }

        /// <param name="rows">Each file with its full path.</param>
        public async Task<IReadOnlyList<ShelfFile>> OfAsync(IReadOnlyList<(FileRecord File, string Path)> rows, IDbSession? session = null)
        {
            IReadOnlyDictionary<Guid, ListedFields> fields =
                await _fieldValues.GetListedAsync(rows.Select(row => row.File).ToList(), session);

            return rows
                .Select(row => StorageContracts.ToShelfFile(row.File, ShelfPath.GetParent(row.Path), fields.GetValueOrDefault(row.File.Id), _derived.Of(row.File)))
                .ToList();
        }
    }
}
