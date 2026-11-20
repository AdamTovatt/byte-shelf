using ByteShelf.Server.Version.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Version.Services
{
    public interface IVersionService
    {
        /// <summary>The running deploy's id, which anyone may read.</summary>
        VersionView GetCurrent();

        /// <summary>The release this server runs, with the deploy id; for signed-in callers only (Version.md says why).</summary>
        ServerVersion GetRelease();
    }
}
