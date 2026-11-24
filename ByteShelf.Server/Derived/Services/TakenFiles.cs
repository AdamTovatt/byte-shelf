using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Plugins.Processes;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>What a maker's run made of contents, out of its process's folder and to be kept once the run is over.</summary>
    /// <param name="Folder">Where the files made are, which notes the contents.</param>
    /// <param name="Files">What is to be kept of each kind the run was for.</param>
    internal sealed record TakenFiles(WorkFolder Folder, IReadOnlyList<KeptFile> Files);
}
