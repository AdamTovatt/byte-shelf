using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using ByteShelf.Server.Access.Models;
using ByteShelf.Server.Shared;
using ByteShelf.Server.Shared.Auth;
using ByteShelfCommon;

namespace ByteShelf.Server.Storage.Services
{
    /// <summary>What an upload link lets its holder do: store one file at <see cref="Path"/>, acting as <see cref="Caller"/>.</summary>
    public sealed record UploadLinkGrant(Guid ShelfId, ShelfCallerReference Caller, string Path, bool Overwrite, string? Description);

    /// <summary>
    /// Signed links to store one file without signing in. A link carries its <see cref="UploadLinkGrant"/>,
    /// signed by <see cref="LinkTokens"/>, so nothing is stored when one is made; the access of whoever
    /// made it is checked again when it is used (<see cref="UploadLinkAccess"/>).
    /// </summary>
    public sealed class UploadLinks
    {
        /// <summary>The route links point at; a link is this, a slash and its token.</summary>
        public const string Route = "api/upload-links";

        // The signing key is derived from this: changing it ends every upload link made before.
        private const string Purpose = "upload";

        // The payload: the shelf, the caller's kind and id, whether it may overwrite, the path's length in
        // bytes, then the path and the description (empty for none), both UTF-8.
        private const int IdBytes = 16;
        private const int ShelfAt = 0;
        private const int CallerKindAt = ShelfAt + IdBytes;
        private const int CallerAt = CallerKindAt + 1;
        private const int OverwriteAt = CallerAt + IdBytes;
        private const int PathLengthAt = OverwriteAt + 1;
        private const int PathAt = PathLengthAt + sizeof(ushort);

        private readonly LinkTokens _tokens;
        private readonly PublicAddress _publicAddress;

        public UploadLinks(LinkTokens tokens, PublicAddress publicAddress)
        {
            _tokens = tokens;
            _publicAddress = publicAddress;
        }

        /// <summary>A link to store a file as <paramref name="request"/> asks, made by <see cref="PublicAddress.LinkTo"/>.</summary>
        public UploadLink Create(Guid shelfId, ShelfCallerReference caller, NormalizedUploadLinkRequest request)
        {
            byte[] path = Encoding.UTF8.GetBytes(request.Path);
            byte[] description = request.Description == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(request.Description);

            byte[] payload = new byte[PathAt + path.Length + description.Length];
            Span<byte> span = payload;
            shelfId.TryWriteBytes(span.Slice(ShelfAt, IdBytes));
            span[CallerKindAt] = (byte)caller.Kind;
            caller.Id.TryWriteBytes(span.Slice(CallerAt, IdBytes));
            span[OverwriteAt] = request.Overwrite ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt16BigEndian(span.Slice(PathLengthAt, sizeof(ushort)), (ushort)path.Length);
            path.CopyTo(span[PathAt..]);
            description.CopyTo(span[(PathAt + path.Length)..]);

            (string token, DateTimeOffset expiresAt) = _tokens.Create(Purpose, payload, request.Lifetime);

            return new UploadLink
            {
                Url = _publicAddress.LinkTo($"/{Route}/{token}"),
                ExpiresAt = expiresAt,
                Path = request.Path,
                Overwrite = request.Overwrite,
            };
        }

        /// <summary>What a link's token grants, when it is one of these links and has not run out.</summary>
        public bool TryRead(string token, [NotNullWhen(true)] out UploadLinkGrant? grant)
        {
            grant = null;
            if (!_tokens.TryRead(Purpose, token, out byte[]? payload) || payload.Length < PathAt)
            {
                return false;
            }

            ReadOnlySpan<byte> span = payload;
            int pathLength = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(PathLengthAt, sizeof(ushort)));
            if (payload.Length < PathAt + pathLength)
            {
                return false;
            }

            ReadOnlySpan<byte> description = span[(PathAt + pathLength)..];
            grant = new UploadLinkGrant(
                new Guid(span.Slice(ShelfAt, IdBytes)),
                new ShelfCallerReference((ShelfCallerKind)span[CallerKindAt], new Guid(span.Slice(CallerAt, IdBytes))),
                Encoding.UTF8.GetString(span.Slice(PathAt, pathLength)),
                span[OverwriteAt] == 1,
                description.IsEmpty ? null : Encoding.UTF8.GetString(description));
            return true;
        }
    }
}
