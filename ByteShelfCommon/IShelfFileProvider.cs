using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ByteShelfCommon
{
    public interface IShelfFileProvider
    {
        Task<IEnumerable<ShelfFileMetadata>> GetFilesAsync(CancellationToken cancellationToken = default);

        Task<ShelfFile> ReadFileAsync(Guid fileId, CancellationToken cancellationToken = default);

        Task<Guid> WriteFileAsync(
            string originalFilename,
            string contentType,
            Stream content,
            CancellationToken cancellationToken = default);

        Task DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default);
    }
} 