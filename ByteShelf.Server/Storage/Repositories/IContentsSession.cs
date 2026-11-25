using EasyReasy.Database;

namespace ByteShelf.Server.Storage.Repositories
{
    /// <summary>
    /// A transaction that may change which contents files hold, as whatever does so requires. Committing it removes
    /// what else the server has of those that were released, and has what is done with those stored done.
    /// </summary>
    public interface IContentsSession : IDbTransactionSession
    {
        /// <summary>A file's contents are no longer those, or no longer plain.</summary>
        void Release(string sha256);

        /// <summary>A live plain file holds contents it did not.</summary>
        void Store(string sha256);
    }
}
