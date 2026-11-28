using ByteShelf.Server.Storage.Models;
using EasyReasy.Database;

namespace ByteShelf.Server.Storage.Repositories
{
    /// <summary>Which contents plain files hold: those something may be read or made from.</summary>
    internal interface IPlainContentsRepository
    {
        /// <summary>The media types plain files, live or in the trash, hold the contents as; none when no plain file does.</summary>
        Task<IReadOnlyList<string>> GetMediaTypesAsync(string sha256, IDbSession? session = null);

        /// <summary>
        /// Contents live plain files of <paramref name="mediaTypes"/> hold, after <paramref name="after"/> in order of
        /// SHA-256, at most <paramref name="limit"/>, each with every media type <see cref="GetMediaTypesAsync"/> answers.
        /// </summary>
        Task<IReadOnlyList<(string Sha256, IReadOnlyList<string> MediaTypes)>> GetLiveAsync(MediaTypeSet mediaTypes, string? after, int limit, IDbSession? session = null);

        /// <summary>Whether a live plain file of <paramref name="mediaTypes"/> holds <paramref name="sha256"/>: one <see cref="GetLiveAsync"/> answers.</summary>
        Task<bool> IsLiveAsync(string sha256, MediaTypeSet mediaTypes, IDbSession? session = null);

        /// <summary>The shelves with a live plain file that holds one of <paramref name="hashes"/>, each once.</summary>
        Task<IReadOnlyList<Guid>> GetShelvesAsync(IReadOnlyCollection<string> hashes, IDbSession? session = null);

        /// <summary>Those of <paramref name="hashes"/> no plain file holds, live or in the trash.</summary>
        Task<IReadOnlyList<string>> GetUnheldAsync(IReadOnlyCollection<string> hashes, IDbSession? session = null);
    }
}
