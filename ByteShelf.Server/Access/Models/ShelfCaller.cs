using ByteShelf.Server.Users.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Access.Models
{
    /// <summary>
    /// Who is calling a shelf endpoint: a signed-in user (perhaps an admin) or a program with a key.
    /// A request carrying a key acts as the key, even when it also carries a user's token.
    /// </summary>
    public sealed class ShelfCaller
    {
        private ShelfCaller(Guid? userId, bool isAdmin, ShelfKey? key)
        {
            UserId = userId;
            IsAdmin = isAdmin;
            Key = key;
        }

        /// <summary>The signed-in user; null for a key.</summary>
        public Guid? UserId { get; }

        /// <summary>Whether the caller is a user with the admin role.</summary>
        public bool IsAdmin { get; }

        /// <summary>The key the request presented; null for a user.</summary>
        public ShelfKey? Key { get; }

        /// <summary>
        /// Who the caller is, as stored with what it starts, such as an upload: <c>user:&lt;id&gt;</c> or
        /// <c>key:&lt;id&gt;</c>.
        /// </summary>
        public string Id => Reference.Kind == ShelfCallerKind.Key ? $"key:{Reference.Id:D}" : $"user:{Reference.Id:D}";

        /// <summary>The caller, to find again later with <c>ShelfCallers.FindAsync</c>.</summary>
        public ShelfCallerReference Reference => Key != null
            ? new ShelfCallerReference(ShelfCallerKind.Key, Key.Id)
            : new ShelfCallerReference(ShelfCallerKind.User, UserId!.Value);

        public static ShelfCaller ForUser(Guid userId, bool isAdmin)
        {
            return new ShelfCaller(userId, isAdmin, key: null);
        }

        /// <summary>A stored user, an admin when they hold the admin role.</summary>
        public static ShelfCaller ForUser(User user)
        {
            return ForUser(user.Id, user.Roles.Contains(UserRoles.Admin));
        }

        public static ShelfCaller ForKey(ShelfKey key)
        {
            return new ShelfCaller(userId: null, isAdmin: false, key);
        }
    }
}
