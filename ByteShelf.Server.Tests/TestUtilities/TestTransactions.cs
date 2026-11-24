using EasyReasy.Database;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>Writes straight to a server's tables.</summary>
    internal static class TestTransactions
    {
        /// <summary>Does something in a scope and transaction of its own.</summary>
        public static async Task<TResult> InTransactionAsync<TResult>(this IServiceProvider services, Func<IServiceProvider, IDbSession, Task<TResult>> write)
        {
            using IServiceScope scope = services.CreateScope();
            await using IDbTransactionSession session = await scope.ServiceProvider.GetRequiredService<IDbSessionFactory>().CreateSessionWithTransactionAsync();
            TResult result = await write(scope.ServiceProvider, session);
            await session.CommitAsync();
            return result;
        }
    }
}
