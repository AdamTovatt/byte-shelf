using ByteShelf.Plugins;
using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Models
{
    /// <summary>One kind a plugin makes (<see cref="DerivedFileDeclaration"/>), its types checked.</summary>
    public sealed record DeclaredKind(DerivedKind Kind, int Version, MediaTypeSet MediaTypes)
    {
        /// <exception cref="ArgumentException">One of the types is no media type or family of them.</exception>
        public static DeclaredKind Of(DerivedFileDeclaration declaration)
        {
            return new DeclaredKind(declaration.Kind, declaration.Version, MediaTypeSet.Of(declaration.MediaTypes));
        }
    }
}
