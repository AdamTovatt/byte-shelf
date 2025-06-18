using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ByteShelfCommon;
using Microsoft.Extensions.Logging;

namespace ByteShelf.Services
{
    public class FileStorageService : IFileStorageService
    {
        private readonly string _storagePath;
        private readonly string _metadataPath;
        private readonly string _binPath;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly ILogger<FileStorageService>? _logger;

        public FileStorageService(string storagePath, ILogger<FileStorageService>? logger = null)
        {
            _storagePath = storagePath;
            _metadataPath = Path.Combine(_storagePath, "metadata");
            _binPath = Path.Combine(_storagePath, "bin");
            _logger = logger;
            
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
            };

            // Ensure directories exist
            Directory.CreateDirectory(_metadataPath);
            Directory.CreateDirectory(_binPath);
            
            _logger?.LogInformation("FileStorageService initialized with storage path: {StoragePath}", _storagePath);
        }

        public async Task<IEnumerable<ShelfFileMetadata>> GetFilesAsync(CancellationToken cancellationToken = default)
        {
            List<ShelfFileMetadata> files = new List<ShelfFileMetadata>();
            
            string[] metadataFiles = Directory.GetFiles(_metadataPath, "*.json");
            _logger?.LogDebug("Found {Count} metadata files", metadataFiles.Length);
            
            foreach (string metadataFile in metadataFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    string json = await File.ReadAllTextAsync(metadataFile, cancellationToken);
                    ShelfFileMetadata? metadata = JsonSerializer.Deserialize<ShelfFileMetadata>(json, _jsonOptions);
                    
                    if (metadata != null)
                        files.Add(metadata);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to read metadata file: {MetadataFile}", metadataFile);
                    // Skip corrupted metadata files
                }
            }

            _logger?.LogDebug("Successfully loaded {Count} valid metadata files", files.Count);
            return files;
        }

        public async Task<ShelfFileMetadata?> GetFileMetadataAsync(Guid fileId, CancellationToken cancellationToken = default)
        {
            string metadataFile = Path.Combine(_metadataPath, $"{fileId}.json");
            
            if (!File.Exists(metadataFile))
            {
                _logger?.LogDebug("Metadata file not found for file ID: {FileId}", fileId);
                return null;
            }

            try
            {
                string json = await File.ReadAllTextAsync(metadataFile, cancellationToken);
                ShelfFileMetadata? metadata = JsonSerializer.Deserialize<ShelfFileMetadata>(json, _jsonOptions);
                
                _logger?.LogDebug("Successfully loaded metadata for file ID: {FileId}", fileId);
                return metadata;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to read metadata for file ID: {FileId}", fileId);
                throw;
            }
        }

        public async Task<Stream> GetChunkAsync(Guid chunkId, CancellationToken cancellationToken = default)
        {
            string chunkFile = Path.Combine(_binPath, $"{chunkId}.bin");
            
            if (!File.Exists(chunkFile))
            {
                _logger?.LogWarning("Chunk file not found: {ChunkId}", chunkId);
                throw new FileNotFoundException($"Chunk {chunkId} not found");
            }

            try
            {
                Stream stream = File.OpenRead(chunkFile);
                _logger?.LogDebug("Successfully opened chunk file: {ChunkId}", chunkId);
                return stream;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to open chunk file: {ChunkId}", chunkId);
                throw;
            }
        }

        public async Task<Guid> SaveChunkAsync(Guid chunkId, Stream chunkData, CancellationToken cancellationToken = default)
        {
            string chunkFile = Path.Combine(_binPath, $"{chunkId}.bin");

            try
            {
                using FileStream fileStream = File.Create(chunkFile);
                await chunkData.CopyToAsync(fileStream, cancellationToken);
                
                _logger?.LogDebug("Successfully saved chunk: {ChunkId}", chunkId);
                return chunkId;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save chunk: {ChunkId}", chunkId);
                throw;
            }
        }

        public async Task SaveFileMetadataAsync(ShelfFileMetadata metadata, CancellationToken cancellationToken = default)
        {
            string metadataFile = Path.Combine(_metadataPath, $"{metadata.Id}.json");
            
            try
            {
                string json = JsonSerializer.Serialize(metadata, _jsonOptions);
                await File.WriteAllTextAsync(metadataFile, json, cancellationToken);
                
                _logger?.LogInformation("Successfully saved metadata for file: {FileId} ({OriginalFilename})", 
                    metadata.Id, metadata.OriginalFilename);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save metadata for file: {FileId}", metadata.Id);
                throw;
            }
        }

        public async Task DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default)
        {
            ShelfFileMetadata? metadata = await GetFileMetadataAsync(fileId, cancellationToken);
            
            if (metadata == null)
            {
                _logger?.LogDebug("File not found for deletion: {FileId}", fileId);
                return;
            }

            int deletedChunks = 0;
            // Delete all chunks
            foreach (Guid chunkId in metadata.ChunkIds)
            {
                string chunkFile = Path.Combine(_binPath, $"{chunkId}.bin");
                if (File.Exists(chunkFile))
                {
                    try
                    {
                        File.Delete(chunkFile);
                        deletedChunks++;
                        _logger?.LogDebug("Deleted chunk: {ChunkId}", chunkId);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to delete chunk: {ChunkId}", chunkId);
                    }
                }
            }

            // Delete metadata file
            string metadataFile = Path.Combine(_metadataPath, $"{fileId}.json");
            if (File.Exists(metadataFile))
            {
                try
                {
                    File.Delete(metadataFile);
                    _logger?.LogInformation("Successfully deleted file: {FileId} ({OriginalFilename}) with {ChunkCount} chunks", 
                        fileId, metadata.OriginalFilename, deletedChunks);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to delete metadata file: {FileId}", fileId);
                    throw;
                }
            }
        }
    }
} 