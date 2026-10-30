using ByteShelf.Server.Version.Models;

namespace ByteShelf.Server.Version.Services
{
    public interface IVersionService
    {
        VersionView GetCurrent();
    }
}
