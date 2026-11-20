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

        /// <param name="grants">Every grant the key holds.</param>
        public static ShelfKeyInfo ToShelfKeyInfo(ShelfKey key, IEnumerable<HeldShelfKeyGrant> grants)
        {
            return new ShelfKeyInfo
            {
                Id = key.Id,
                Name = key.Name,
                Grants = grants.Select(ToShelfKeyGrantInfo).ToList(),
                CreatedAt = key.CreatedAt,
            };
        }

        public static ShelfKeyGrantInfo ToShelfKeyGrantInfo(HeldShelfKeyGrant held)
        {
            return new ShelfKeyGrantInfo
            {
                KeyId = held.Grant.KeyId,
                ShelfId = held.Grant.ShelfId,
                ShelfName = held.ShelfName,
                Permission = held.Grant.Permission,
                CreatedAt = held.Grant.CreatedAt,
                UpdatedAt = held.Grant.UpdatedAt,
            };
        }
    }
}
