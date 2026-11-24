using ByteShelfCommon;

namespace ByteShelf.Plugins
{
    /// <summary>Takes the derived files a maker makes in one run.</summary>
    public interface IMadeFiles
    {
        /// <summary>
        /// Has <paramref name="write"/> write the file of <paramref name="kind"/> to a seekable stream, which is closed
        /// after. The kind is made once it has; what it throws is thrown on, and a file it did not finish is not kept.
        /// A thumbnail may take 1 MiB, a preview 16 MiB, a full 512 MiB and an agent's 1 MiB: a write beyond that
        /// throws an <see cref="IOException"/>.
        /// </summary>
        /// <param name="kind">One of the kinds the run was asked for, each made at most once.</param>
        /// <param name="mediaType">The file's media type.</param>
        /// <param name="original">What it is made from, when the maker can tell.</param>
        /// <param name="write">Writes the file.</param>
        /// <exception cref="ArgumentException">The kind was not asked for.</exception>
        /// <exception cref="InvalidOperationException">The kind is made already.</exception>
        Task AddAsync(DerivedKind kind, string mediaType, DerivedOriginal? original, Func<Stream, Task> write);
    }
}
