namespace ByteShelfCommon
{
    /// <summary>The roles a user can have.</summary>
    public static class UserRoles
    {
        /// <summary>Creates shelves and users, and reaches every shelf without being granted access.</summary>
        public const string Admin = "admin";

        /// <summary>Every role there is.</summary>
        public static IReadOnlyList<string> All { get; } = new[] { Admin };

        /// <summary>The distinct roles in <paramref name="roles"/>, none when it is null.</summary>
        /// <exception cref="ArgumentException">One of them is not in <see cref="All"/>.</exception>
        public static IReadOnlyList<string> Normalize(IEnumerable<string>? roles)
        {
            List<string> distinct = (roles ?? Enumerable.Empty<string>()).Distinct().ToList();
            foreach (string role in distinct)
            {
                if (!All.Contains(role))
                {
                    throw new ArgumentException($"'{role}' is not a role. Roles: {string.Join(", ", All)}.");
                }
            }

            return distinct;
        }
    }
}
