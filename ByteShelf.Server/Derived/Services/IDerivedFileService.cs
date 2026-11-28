using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>
    /// The derived files of files. One that is missing is made, ahead of what is made in the background, and
    /// waited for a while; but for a kind only ever made in the background (<see cref="MakingStart.MadeWhenAsked"/>),
    /// which is left to be.
    /// </summary>
    public interface IDerivedFileService
    {
        /// <exception cref="Shared.Errors.NotFoundException">There is no such live file, it is encrypted, no plugin makes the kind from its type, or it could not be made.</exception>
        /// <exception cref="Shared.Errors.RetryLaterException">It is still being made, which goes on, or is still to be made in the background.</exception>
        /// <exception cref="Plugins.Processes.PluginUnavailableException">The plugin could not run, or, for a kind only made in the background, could not when it last had to.</exception>
        Task<DerivedFile> GetAsync(Shelf shelf, EntryReference reference, DerivedKind kind, CancellationToken cancellationToken);

        /// <summary>As <see cref="GetAsync"/>, of a file deletion <paramref name="deletionId"/> removed.</summary>
        Task<DerivedFile> GetDeletedAsync(Shelf shelf, Guid deletionId, Guid fileId, DerivedKind kind, CancellationToken cancellationToken);
    }
}
