using System.Data.Common;
using ByteShelf.Server.Derived.Models;
using ByteShelfCommon;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Derived.Repositories
{
    public sealed class DerivedSizeRepository : RepositoryBase, IDerivedSizeRepository
    {
        public DerivedSizeRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory)
            : base(dataSource, sessionFactory)
        {
        }

        /// <inheritdoc />
        public async Task<long?> GetAsync(string sha256, DerivedKind kind, IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT size_bytes FROM derived_size WHERE sha256 = @{nameof(sha256)} AND kind = @Kind";
                return await dbSession.Connection.QuerySingleOrDefaultAsync<long?>(query, new { sha256, Kind = DerivedKinds.NameOf(kind) }, transaction: dbSession.Transaction);
            }, session);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyDictionary<(string Sha256, DerivedKind Kind), long>> GetAllAsync(IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string query = $"SELECT sha256 AS {nameof(SizeEntity.Sha256)}, kind AS {nameof(SizeEntity.Kind)}, size_bytes AS {nameof(SizeEntity.SizeBytes)} FROM derived_size";
                IEnumerable<SizeEntity> rows = await dbSession.Connection.QueryAsync<SizeEntity>(query, transaction: dbSession.Transaction);
                return (IReadOnlyDictionary<(string Sha256, DerivedKind Kind), long>)rows.ToDictionary(row => (row.Sha256, DerivedKinds.Parse(row.Kind)), row => row.SizeBytes);
            }, session);
        }

        /// <inheritdoc />
        public async Task SetAsync(string sha256, DerivedKind kind, long sizeBytes, IDbSession session)
        {
            string query = $@"
                INSERT INTO derived_size (sha256, kind, size_bytes) VALUES (@{nameof(sha256)}, @Kind, @{nameof(sizeBytes)})
                ON CONFLICT (sha256, kind) DO UPDATE SET size_bytes = excluded.size_bytes";
            await session.Connection.ExecuteAsync(query, new { sha256, Kind = DerivedKinds.NameOf(kind), sizeBytes }, transaction: session.Transaction);
        }

        /// <inheritdoc />
        public async Task RemoveAsync(string sha256, DerivedKind kind, IDbSession session)
        {
            string query = $"DELETE FROM derived_size WHERE sha256 = @{nameof(sha256)} AND kind = @Kind";
            await session.Connection.ExecuteAsync(query, new { sha256, Kind = DerivedKinds.NameOf(kind) }, transaction: session.Transaction);
        }

        /// <inheritdoc />
        public async Task RemoveAsync(string sha256, IDbSession session)
        {
            string query = $"DELETE FROM derived_size WHERE sha256 = @{nameof(sha256)}";
            await session.Connection.ExecuteAsync(query, new { sha256 }, transaction: session.Transaction);
        }

        /// <inheritdoc />
        public async Task RemoveAllAsync(IDbSession session)
        {
            await session.Connection.ExecuteAsync("DELETE FROM derived_size", transaction: session.Transaction);
        }

        private sealed class SizeEntity
        {
            public string Sha256 { get; set; } = string.Empty;
            public string Kind { get; set; } = string.Empty;
            public long SizeBytes { get; set; }
        }
    }
}
