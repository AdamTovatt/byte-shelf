using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Metadata.Repositories
{
    /// <summary>
    /// The <c>plugin_field_value</c> table, with <c>plugin_declared_fields</c> saying what it was taken by: the
    /// values of installed plugins' declared fields. Nothing here is logged, as every server takes its own.
    /// </summary>
    public interface IPluginFieldValueRepository
    {
        /// <summary>Takes the values of a document about contents as written, over those it had; none for a plugin that is not installed.</summary>
        Task TakeAsync(ContentPluginMetadata row, IDbSession session);

        /// <summary>Takes the values of a document about a file as written, over those it had; none for a plugin that is not installed.</summary>
        Task TakeAsync(FilePluginMetadata row, IDbSession session);

        /// <summary>The values listings carry of each of <paramref name="files"/> that has any, by the file's id, then plugin id and field. An encrypted file has none from contents.</summary>
        Task<IReadOnlyDictionary<Guid, ListedFields>> GetListedAsync(IReadOnlyCollection<FileRecord> files, IDbSession? session = null);

        /// <summary>The plugins whose values were taken, each with the fields they were taken by, as written by <see cref="SetDeclaredFieldsAsync"/>.</summary>
        Task<IReadOnlyDictionary<string, string>> GetDeclaredFieldsAsync(IDbSession session);

        /// <summary>Says the plugin's values were taken by <paramref name="fields"/>.</summary>
        Task SetDeclaredFieldsAsync(string pluginId, string fields, IDbSession session);

        /// <summary>Removes the plugin's values, and that it had any.</summary>
        Task DropAsync(string pluginId, IDbSession session);
    }
}
