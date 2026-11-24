using ByteShelf.Server.Metadata.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Derived.Repositories
{
    /// <summary>Which contents something may be derived from: those a plain file holds.</summary>
    internal interface ISourceContentsRepository
    {
        /// <summary>The media types plain files, live or in the trash, hold the contents as; none when no plain file does.</summary>
        Task<IReadOnlyList<string>> GetMediaTypesAsync(string sha256, IDbSession? session = null);

        /// <summary>
        /// Contents live plain files of <paramref name="mediaTypes"/> hold, after <paramref name="after"/> in order of
        /// SHA-256, at most <paramref name="limit"/>, each with every media type <see cref="GetMediaTypesAsync"/> answers.
        /// </summary>
        Task<IReadOnlyList<(string Sha256, IReadOnlyList<string> MediaTypes)>> GetLiveAsync(MediaTypeSet mediaTypes, string? after, int limit, IDbSession? session = null);

        /// <summary>Those of <paramref name="hashes"/> no plain file holds, live or in the trash.</summary>
        Task<IReadOnlyList<string>> GetUnheldAsync(IReadOnlyCollection<string> hashes, IDbSession? session = null);
    }
}
