using ByteShelf.Server.Access.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Access.Services
{
    /// <summary>Turns access rows into the contract types the API answers with.</summary>
    public static class AccessContracts
    {
        public static ShelfAccessGrant ToShelfAccessGrant(ShelfGrant grant, string email, string displayName)
        {
            return new ShelfAccessGrant
            {
                ShelfId = grant.ShelfId,
                UserId = grant.UserId,
                Email = email,
                DisplayName = displayName,
                Permission = grant.Permission,
                CreatedAt = grant.CreatedAt,
                UpdatedAt = grant.UpdatedAt,
            };
        }

        public static ShelfKeyInfo ToShelfKeyInfo(ShelfKey key)
        {
            return new ShelfKeyInfo
            {
                Id = key.Id,
                ShelfId = key.ShelfId,
                Name = key.Name,
                Permission = key.Permission,
                CreatedAt = key.CreatedAt,
            };
        }
    }
}
