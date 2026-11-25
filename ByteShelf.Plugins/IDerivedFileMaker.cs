using ByteShelfCommon;

namespace ByteShelf.Plugins
{
    /// <summary>
    /// A plugin's maker of derived files (<see cref="IPluginBuilder.AddMaker{TMaker}"/>), such as thumbnails. The
    /// server has it make them from plain files' contents, never an encrypted one's, and keeps them until no
    /// plain file holds the contents any more. It runs in a child process of the server's, one thing at a time,
    /// which may take 1.5 GiB of memory, so contents that hang or crash it take nothing else down; what it makes
    /// or reads must be kept nowhere else, or it would outlive the file being encrypted.
    /// </summary>
    public interface IDerivedFileMaker
    {
        /// <summary>
        /// Makes the <paramref name="kinds"/>, all ones it was added for and from one read, from
        /// <paramref name="contents"/>, a seekable stream at its start, each read of which is asked of the server, and
        /// writes each to <paramref name="made"/>. A kind it leaves out, or all of them when it throws, crashes,
        /// takes too much memory or is ended for taking over 60 seconds, could not be made, and is not tried again
        /// from the same contents until that kind's version changes. It may write files in the working folder, which is emptied after. Processes
        /// it starts are ended with it, but on Windows may outlive it, so a maker there starts none.
        /// </summary>
        Task MakeAsync(Stream contents, IReadOnlyList<DerivedKind> kinds, IMadeFiles made);
    }
}
