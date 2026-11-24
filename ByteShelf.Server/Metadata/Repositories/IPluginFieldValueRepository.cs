using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Metadata.Repositories
{
    /// <summary>
    /// The <c>plugin_field_value</c> table, with <c>plugin_values_taken</c> saying what they were taken by: the
    /// values of installed plugins' declared fields. Nothing here is logged, as every server takes its own.
    /// Where the plugins are not known (<see cref="KnownPlugins"/>), values can be removed, and not read or taken.
    /// </summary>
    public interface IPluginFieldValueRepository
    {
        /// <summary>Takes the values of a document about contents as written, over those it had; none for a plugin that is not installed.</summary>
        /// <exception cref="InvalidOperationException">There is a document, and the plugins are not known: its values would be missing, and the next start take them for current.</exception>
        Task TakeAsync(ContentPluginMetadata row, IDbSession session);

        /// <summary>Takes the values of a document about a file as written, over those it had; none for a plugin that is not installed.</summary>
        /// <exception cref="InvalidOperationException">There is a document, and the plugins are not known: its values would be missing, and the next start take them for current.</exception>
        Task TakeAsync(FilePluginMetadata row, IDbSession session);

        /// <summary>The values listings carry of each of <paramref name="files"/> that has any, by the file's id, then plugin id and field. An encrypted file has none from contents.</summary>
        /// <exception cref="InvalidOperationException">The plugins are not known.</exception>
        Task<IReadOnlyDictionary<Guid, ListedFields>> GetListedAsync(IReadOnlyCollection<FileRecord> files, IDbSession? session = null);

        /// <summary>The plugins whose values were taken, each with what they were taken by, as written by <see cref="SetTakenByAsync"/>.</summary>
        Task<IReadOnlyDictionary<string, string>> GetTakenByAsync(IDbSession session);

        /// <summary>Says the plugin's values were taken by <paramref name="takenBy"/>, which tells one way of taking them from another.</summary>
        Task SetTakenByAsync(string pluginId, string takenBy, IDbSession session);

        /// <summary>Removes the plugin's values, and that it had any.</summary>
        Task DropAsync(string pluginId, IDbSession session);
    }
}
