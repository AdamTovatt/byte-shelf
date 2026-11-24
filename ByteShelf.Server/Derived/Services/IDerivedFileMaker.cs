using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Metadata.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>
    /// A plugin's maker of derived files. The server asks what it makes, and has it make them in a child
    /// process, one at a time, so contents that hang or crash it take nothing else down. A child process only
    /// ever runs it: the server has no instance of one.
    /// </summary>
    public interface IDerivedFileMaker
    {
        /// <summary>The kinds it makes, each once.</summary>
        IReadOnlyList<DerivedFileDeclaration> Makes { get; }

        /// <summary>
        /// Makes the <paramref name="kinds"/>, all ones it declares, from <paramref name="contents"/>, a seekable
        /// stream at its start. A kind it leaves out, or all of them when it throws, crashes or is killed for
        /// taking over 60 seconds, could not be made, and is not tried again from the same contents until that
        /// kind's version changes; nor could one larger than its kind may be (<see cref="DerivedKinds.MaxBytesOf"/>).
        /// Processes it starts are ended with it, but on Windows may outlive it, so a maker there starts none.
        /// </summary>
        Task<IReadOnlyList<MadeFile>> MakeAsync(Stream contents, IReadOnlyList<DerivedKind> kinds, CancellationToken cancellationToken);
    }

    /// <summary>One kind a plugin makes.</summary>
    /// <param name="Kind">The kind.</param>
    /// <param name="MediaTypes">The types of the files it is made from.</param>
    /// <param name="Version">What another version made, or failed to make, is made again.</param>
    public sealed record DerivedFileDeclaration(DerivedKind Kind, MediaTypeSet MediaTypes, int Version);

    /// <summary>A derived file as its maker answers it.</summary>
    /// <param name="Kind">The kind it is.</param>
    /// <param name="MediaType">Its media type.</param>
    /// <param name="Contents">Its bytes.</param>
    /// <param name="Original">What it was made from.</param>
    public sealed record MadeFile(DerivedKind Kind, string MediaType, byte[] Contents, DerivedOriginal? Original);
}
