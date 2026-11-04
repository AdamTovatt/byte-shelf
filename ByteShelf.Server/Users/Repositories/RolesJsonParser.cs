using System.Text.Json;

namespace ByteShelf.Server.Users.Repositories
{
    /// <summary>Converts between the <c>roles</c> column, a JSON array of strings, and a role list.</summary>
    public static class RolesJsonParser
    {
        /// <summary>
        /// Parses the column. Every writer stores a valid array, so anything else means the row is
        /// damaged; that is reported rather than read as "no roles", which would silently take an
        /// admin's access away.
        /// </summary>
        /// <exception cref="InvalidOperationException">The value is not a JSON array of strings.</exception>
        public static IReadOnlyList<string> Parse(string rolesJson)
        {
            try
            {
                return JsonSerializer.Deserialize<string[]>(rolesJson)
                    ?? throw new InvalidOperationException("The roles column holds JSON null.");
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException($"The roles column holds '{rolesJson}', which is not a JSON array of strings.", exception);
            }
        }

        public static string Serialize(IEnumerable<string> roles)
        {
            return JsonSerializer.Serialize(roles.ToArray());
        }
    }
}
