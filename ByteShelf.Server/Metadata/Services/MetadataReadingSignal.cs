using ByteShelf.Server.Shared.Concurrency;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>Tells the job <c>metadata.reading</c> that contents were stored, so it reads them now rather than at its next pass.</summary>
    public sealed class MetadataReadingSignal : IDisposable
    {
        internal WakeSignal Wake { get; } = new WakeSignal();

        public void ContentsStored()
        {
            Wake.Wake();
        }

        public void Dispose()
        {
            Wake.Dispose();
        }
    }
}
