using ByteShelf.Server.Users.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Users.Services
{
    /// <summary>Turns users into the contract type the API answers with.</summary>
    public static class UserContracts
    {
        public static UserInfo ToUserInfo(User user)
        {
            return new UserInfo
            {
                Id = user.Id,
                Email = user.Email,
                DisplayName = user.DisplayName,
                Roles = user.Roles,
            };
        }
    }
}
