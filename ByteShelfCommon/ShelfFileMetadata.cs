using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ByteShelfCommon
{
    public class ShelfFileMetadata
    {
        public Guid Id { get; set; }

        public string OriginalFilename { get; set; }

        public string ContentType { get; set; }

        public long FileSize { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public List<Guid> ChunkIds { get; set; }

        public ShelfFileMetadata(
            Guid id,
            string originalFilename,
            string contentType,
            long fileSize,
            List<Guid> chunkIds)
        {
            Id = id;
            OriginalFilename = originalFilename;
            ContentType = contentType;
            FileSize = fileSize;
            ChunkIds = chunkIds;
            CreatedAt = DateTimeOffset.UtcNow;
        }
    }
} 