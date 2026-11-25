using ByteShelfCommon;

namespace ByteShelf.Plugins
{
    /// <summary>Takes the derived files a maker makes in one run.</summary>
    public interface IMadeFiles
    {
        /// <summary>
        /// Has <paramref name="write"/> write the file of <paramref name="kind"/> to a seekable stream, which is closed
        /// after. The kind is made once it has; what it throws is thrown on, and a file it did not finish is not kept.
        /// A write beyond what the kind may take (<see cref="DerivedFileLimits.MaxBytesOf"/>) throws an
        /// <see cref="IOException"/>, which a native library that writes swallows: have it write to memory first, and
        /// only what fits here.
        /// </summary>
        /// <param name="kind">One of the kinds the run was asked for, each made at most once.</param>
        /// <param name="mediaType">The file's media type.</param>
        /// <param name="write">Writes the file.</param>
        /// <exception cref="ArgumentException">The kind was not asked for.</exception>
        /// <exception cref="InvalidOperationException">The kind is made already.</exception>
        Task AddAsync(DerivedKind kind, string mediaType, Func<Stream, Task> write);
    }
}
