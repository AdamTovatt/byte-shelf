using System.Text.Json;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Repositories;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>
    /// Brings the values of plugins' declared fields in step with what is installed, before the server takes
    /// requests: one that declares other fields than its values were taken by, or by other rules of the server's,
    /// has them taken again from its documents, and one that is gone loses them, so nothing of it is found or listed. Each plugin in a
    /// transaction of its own, so a start that stops half-way goes on from there.
    /// </summary>
    internal sealed class PluginFieldIndex
    {
        /// <summary>How many documents one query reads.</summary>
        internal const int BatchSize = 100;

        /// <summary>
        /// The server's own rules for what it takes and keeps of a field: <see cref="PluginFieldValue.Of"/>, the
        /// folding and cutting of text, the limit on what is listed and the JSON that is. Raised when one
        /// changes, which has every value taken again.
        /// </summary>
        internal const int Version = 1;

        private readonly InstalledPlugins _plugins;
        private readonly IPluginFieldValueRepository _values;
        private readonly IContentPluginMetadataRepository _contentDocuments;
        private readonly IFilePluginMetadataRepository _fileDocuments;
        private readonly IDbSessionFactory _sessionFactory;

        public PluginFieldIndex(
            InstalledPlugins plugins,
            IPluginFieldValueRepository values,
            IContentPluginMetadataRepository contentDocuments,
            IFilePluginMetadataRepository fileDocuments,
            IDbSessionFactory sessionFactory)
        {
            _plugins = plugins;
            _values = values;
            _contentDocuments = contentDocuments;
            _fileDocuments = fileDocuments;
            _sessionFactory = sessionFactory;
        }

        public async Task BringUpToDateAsync(CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<string, string> takenBy;
            await using (IDbSession session = await _sessionFactory.CreateSessionAsync())
            {
                takenBy = await _values.GetTakenByAsync(session);
            }

            foreach (string gone in takenBy.Keys.Where(id => _plugins.Find(id) == null))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync();
                await _values.DropAsync(gone, session);
                await session.CommitAsync();
            }

            foreach (PluginDeclaration plugin in _plugins.All)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string takenNow = Describe(plugin);
                if (takenBy.GetValueOrDefault(plugin.Id) != takenNow)
                {
                    await TakeAgainAsync(plugin.Id, takenNow, cancellationToken);
                }
            }
        }

        /// <summary>What a plugin's values are taken by: <see cref="Version"/> and the fields it declares, whose labels may change without a value changing.</summary>
        private static string Describe(PluginDeclaration plugin)
        {
            return JsonSerializer.Serialize(new
            {
                Version,
                Fields = plugin.Fields.Select(field => new { field.Name, field.Kind, field.Document, field.Searched, field.Listed }),
            });
        }

        private async Task TakeAgainAsync(string pluginId, string takenBy, CancellationToken cancellationToken)
        {
            await using IDbTransactionSession session = await _sessionFactory.CreateSessionWithTransactionAsync();
            await _values.DropAsync(pluginId, session);

            await TakeAllAsync<ContentPluginMetadata>(
                last => _contentDocuments.GetOfPluginAsync(pluginId, last?.Sha256, BatchSize, session),
                document => _values.TakeAsync(document, session),
                cancellationToken);
            await TakeAllAsync<FilePluginMetadata>(
                last => _fileDocuments.GetOfPluginAsync(pluginId, last?.FileId, BatchSize, session),
                document => _values.TakeAsync(document, session),
                cancellationToken);

            await _values.SetTakenByAsync(pluginId, takenBy, session);
            await session.CommitAsync();
        }

        /// <param name="pageAfter">The page after a document; the first after null.</param>
        private static async Task TakeAllAsync<TDocument>(
            Func<TDocument?, Task<IReadOnlyList<TDocument>>> pageAfter,
            Func<TDocument, Task> takeAsync,
            CancellationToken cancellationToken)
            where TDocument : class
        {
            TDocument? last = null;
            while (await pageAfter(last) is { Count: > 0 } page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (TDocument document in page)
                {
                    await takeAsync(document);
                }

                last = page[^1];
            }
        }
    }
}
