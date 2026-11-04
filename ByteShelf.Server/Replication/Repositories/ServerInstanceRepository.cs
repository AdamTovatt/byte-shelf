using System.Data.Common;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Replication.Repositories
{
    public class ServerInstanceRepository : RepositoryBase, IServerInstanceRepository
    {
        public ServerInstanceRepository(DbDataSource dataSource, IDbSessionFactory sessionFactory)
            : base(dataSource, sessionFactory)
        {
        }

        /// <inheritdoc />
        public async Task<string> GetInstanceIdAsync(IDbSession? session = null)
        {
            return await UseSessionAsync(async (dbSession) =>
            {
                string newId = Guid.NewGuid().ToString("N");
                string query = $@"
                    INSERT INTO server_instance (singleton, id) VALUES (1, @{nameof(newId)}) ON CONFLICT (singleton) DO NOTHING;
                    SELECT id FROM server_instance";

                return await dbSession.Connection.QuerySingleAsync<string>(query, new { newId }, transaction: dbSession.Transaction);
            }, session);
        }
    }
}
