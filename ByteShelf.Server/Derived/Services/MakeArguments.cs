using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>What the server asks a maker's process to make.</summary>
    /// <param name="Kinds">Each kind at the version the server knows: another plugin may be at the location by now.</param>
    internal sealed record MakeArguments(IReadOnlyList<RequestedKind> Kinds);

    internal sealed record RequestedKind(DerivedKind Kind, int Version);
}
