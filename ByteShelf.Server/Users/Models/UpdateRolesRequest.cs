namespace ByteShelf.Server.Users.Models
{
    public class UpdateRolesRequest
    {
        public required IEnumerable<string> Roles { get; init; }
    }
}
