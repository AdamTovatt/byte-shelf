using ByteShelfCommon;

namespace ByteShelfClient.Http
{
    /// <summary>
    /// Where processes sharing one signed-in session keep its tokens, such as a file several runs of a
    /// command-line tool read. A refresh token works once, so a <see cref="UserSession"/> with a store
    /// renews only while holding <see cref="LockAsync"/>, and first takes up tokens another process
    /// renewed to.
    /// </summary>
    public interface ISharedSessionStore
    {
        /// <summary>Holds the store for one renewal at a time, across every process sharing it.</summary>
        Task<IAsyncDisposable> LockAsync(CancellationToken cancellationToken);

        /// <summary>The tokens kept now, or null when the session is no longer kept, such as after signing out.</summary>
        Task<SessionTokens?> LoadAsync(CancellationToken cancellationToken);

        /// <summary>Keeps renewed tokens; called while the lock is held.</summary>
        Task SaveAsync(SessionTokens tokens, CancellationToken cancellationToken);

        /// <summary>Stops keeping the session, once it has been signed out; called while the lock is held.</summary>
        Task ClearAsync(CancellationToken cancellationToken);
    }
}
