using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Repositories;
using ByteShelf.Server.Replication.Services;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>
    /// One pass of the job <c>metadata.plugin_reading</c>: runs each plugin's reader in turn. A job of its own, so
    /// that what plugins have yet to read never delays the server's own reading. A replica takes the rows from its
    /// primary's change log, so it does nothing.
    /// </summary>
    internal sealed class PluginMetadataReading
    {
        private readonly IPluginReaders _readers;
        private readonly IContentPluginMetadataRepository _documents;
        private readonly ContentReading _reading;
        private readonly ReplicationState _replicationState;

        public PluginMetadataReading(IPluginReaders readers, IContentPluginMetadataRepository documents, ContentReading reading, ReplicationState replicationState)
        {
            _readers = readers;
            _documents = documents;
            _reading = reading;
            _replicationState = replicationState;
        }

        public async Task<PluginMetadataReadingSummary> RunAsync(CancellationToken cancellationToken)
        {
            if (!await _replicationState.AllowsWritesAsync())
            {
                return new PluginMetadataReadingSummary(OnReplica: true, Read: 0, Failed: 0);
            }

            PluginMetadataReadingSummary summary = new PluginMetadataReadingSummary(OnReplica: false, Read: 0, Failed: 0);
            foreach (PluginReader reader in _readers.All)
            {
                (int read, int failed) = await _reading.RunAsync(new PluginContentReader(reader, _documents), cancellationToken);
                summary = summary with { Read = summary.Read + read, Failed = summary.Failed + failed };
            }

            return summary;
        }
    }
}
