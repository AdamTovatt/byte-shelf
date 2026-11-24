using ByteShelfCommon;

namespace ByteShelf.Plugins
{
    /// <summary>One kind of derived file a plugin makes.</summary>
    /// <param name="Kind">The kind.</param>
    /// <param name="Version">What another version made, or failed to make, is made again.</param>
    /// <param name="MediaTypes">The media types of the files it is made from, lowercase: <c>image/png</c>, or a family as <c>image/*</c>.</param>
    public sealed record DerivedFileDeclaration(DerivedKind Kind, int Version, IReadOnlyList<string> MediaTypes);
}
