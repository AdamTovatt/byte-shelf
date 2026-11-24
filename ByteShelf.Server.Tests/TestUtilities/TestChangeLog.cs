using ByteShelf.Server.ChangeLog.Models;
using ByteShelf.Server.ChangeLog.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.TestUtilities
{
    internal static class TestChangeLog
    {
        /// <summary>The newest entry of every entity in a server's log, as a whole copy reads them.</summary>
        public static async Task<IReadOnlyList<ChangeLogEntry>> GetLatestStatesAsync(this IServiceProvider services)
        {
            return (await services.InTransactionAsync((provider, session) => provider.GetRequiredService<IChangeLogRepository>()
                .GetLatestStatesPageAsync(0, new ChangeLogPageLimit(1000, long.MaxValue), session))).Entries;
        }
    }
}
