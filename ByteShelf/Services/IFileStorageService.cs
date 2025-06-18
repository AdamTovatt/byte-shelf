using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ByteShelfCommon;

namespace ByteShelf.Services
{
    public interface IFileStorageService
    {
        Task<IEnumerable<ShelfFileMetadata>> GetFilesAsync(CancellationToken cancellationToken = default);
        
        Task<ShelfFileMetadata?> GetFileMetadataAsync(Guid fileId, CancellationToken cancellationToken = default);
        
        Task<Stream> GetChunkAsync(Guid chunkId, CancellationToken cancellationToken = default);
        
        Task<Guid> SaveChunkAsync(Guid chunkId, Stream chunkData, CancellationToken cancellationToken = default);
        
        Task SaveFileMetadataAsync(ShelfFileMetadata metadata, CancellationToken cancellationToken = default);
        
        Task DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default);
    }
} 