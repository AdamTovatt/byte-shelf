using ByteShelf.Plugins;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>One kind a maker's process made, as it answers it: its contents are in the folder, in a file of the kind's name.</summary>
    internal sealed record MadeInFolder(DerivedKind Kind, string MediaType, DerivedOriginal? Original);
}
