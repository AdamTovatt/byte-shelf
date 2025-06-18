using System;
using System.IO;

namespace ByteShelfCommon
{
    public class ShelfFile : IDisposable
    {
        public ShelfFileMetadata Metadata { get; }

        private readonly IContentProvider _contentProvider;

        public ShelfFile(
            ShelfFileMetadata metadata,
            IContentProvider contentProvider)
        {
            Metadata = metadata;
            _contentProvider = contentProvider;
        }

        public Stream GetContentStream()
        {
            return _contentProvider.GetStream();
        }

        public void Dispose()
        {
            if (_contentProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
            GC.SuppressFinalize(this);
        }
    }
} 