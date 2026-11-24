using ByteShelf.Server.Metadata.Repositories;
using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>Files as listings give them: with the values of the fields plugins declare for listings, read in one query.</summary>
    public class ListedFiles
    {
        private readonly IPluginFieldValueRepository _fieldValues;

        public ListedFiles(IPluginFieldValueRepository fieldValues)
        {
            _fieldValues = fieldValues;
        }

        /// <param name="rows">Each file with its full path.</param>
        public async Task<IReadOnlyList<ShelfFile>> OfAsync(IReadOnlyList<(FileRecord File, string Path)> rows, IDbSession? session = null)
        {
            IReadOnlyDictionary<Guid, ListedFields> fields =
                await _fieldValues.GetListedAsync(rows.Select(row => row.File).ToList(), session);

            return rows
                .Select(row => StorageContracts.ToShelfFile(row.File, ShelfPath.GetParent(row.Path), fields.GetValueOrDefault(row.File.Id)))
                .ToList();
        }
    }
}
