using System.Data.Common;
using System.Text.Json;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Repositories;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Derived.Repositories
{
    internal sealed class SourceContentsRepository : RepositoryBase, ISourceContentsRepository
    {
        public SourceContentsRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory)
            : base(dataSource, sessionFactory)
        {
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> GetMediaTypesAsync(string sha256, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT DISTINCT media_type FROM file WHERE sha256 = @{nameof(sha256)} AND encrypted = 0 ORDER BY media_type";
                IEnumerable<string> mediaTypes = await dbSession.Connection.QueryAsync<string>(query, new { sha256 }, transaction: dbSession.Transaction);
                return (IReadOnlyList<string>)mediaTypes.ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<(string Sha256, IReadOnlyList<string> MediaTypes)>> GetLiveAsync(MediaTypeSet mediaTypes, string? after, int limit, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $@"
                    SELECT DISTINCT file.sha256 AS {nameof(HeldTypeEntity.Sha256)}, file.media_type AS {nameof(HeldTypeEntity.MediaType)}
                    FROM file
                    WHERE file.encrypted = 0 AND file.sha256 IN ({PlainContents.LiveSql(mediaTypes)})
                    ORDER BY file.sha256, file.media_type";

                IEnumerable<HeldTypeEntity> rows = await dbSession.Connection.QueryAsync<HeldTypeEntity>(
                    query, new { after, limit }, transaction: dbSession.Transaction);

                return (IReadOnlyList<(string Sha256, IReadOnlyList<string> MediaTypes)>)rows
                    .GroupBy(row => row.Sha256, StringComparer.Ordinal)
                    .Select(group => (group.Key, (IReadOnlyList<string>)group.Select(row => row.MediaType).ToList()))
                    .ToList();
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> GetUnheldAsync(IReadOnlyCollection<string> hashes, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string hashesJson = JsonSerializer.Serialize(hashes);
                string query = $"SELECT value FROM json_each(@{nameof(hashesJson)}) AS hash WHERE NOT {PlainContents.HeldByFile("hash.value")}";
                IEnumerable<string> unheld = await dbSession.Connection.QueryAsync<string>(query, new { hashesJson }, transaction: dbSession.Transaction);
                return (IReadOnlyList<string>)unheld.ToList();
            }, session);
        }

        private sealed class HeldTypeEntity
        {
            public string Sha256 { get; set; } = string.Empty;
            public string MediaType { get; set; } = string.Empty;
        }
    }
}
