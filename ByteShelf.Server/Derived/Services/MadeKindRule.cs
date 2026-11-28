using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>
    /// The server's rule of which kind a maker may add of a run (<c>IMadeFiles.AddAsync</c>): one it was asked for, once;
    /// each within its size (<see cref="DerivedFileLimits.MaxBytesOf"/>) as it is written. Compiled into
    /// ByteShelf.Plugins.Testing too, so a plugin's tests hold its maker to this one copy.
    /// </summary>
    internal static class MadeKindRule
    {
        /// <exception cref="ArgumentException"><paramref name="kind"/> was not asked for.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="kind"/> is made already.</exception>
        public static void ThrowIfNotAddable(DerivedKind kind, IReadOnlyList<DerivedKind> asked, bool isMadeAlready)
        {
            if (!asked.Contains(kind))
            {
                throw new ArgumentException($"The kind {kind} was not asked for.", nameof(kind));
            }

            if (isMadeAlready)
            {
                throw new InvalidOperationException($"The kind {kind} is made already.");
            }
        }
    }
}
