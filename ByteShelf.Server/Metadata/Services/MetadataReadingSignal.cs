using ByteShelf.Server.Shared.Concurrency;
using ByteShelf.Server.Storage.Services;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>Tells the jobs that read contents that some were stored, so they read them now rather than at their next pass.</summary>
    public sealed class MetadataReadingSignal : IStoredContentsListener, IDisposable
    {
        /// <summary>Wakes the job <c>metadata.reading</c>.</summary>
        internal WakeSignal Wake { get; } = new WakeSignal();

        /// <summary>Wakes the job <c>metadata.plugin_reading</c>; one of its own, as a wake reaches one waiter.</summary>
        internal WakeSignal PluginWake { get; } = new WakeSignal();

        public void Stored(string sha256)
        {
            Wake.Wake();
            PluginWake.Wake();
        }

        public void Dispose()
        {
            Wake.Dispose();
            PluginWake.Dispose();
        }
    }
}
