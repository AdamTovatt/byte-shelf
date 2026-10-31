using System.Text.RegularExpressions;

namespace ByteShelf.Server.Shared.Database
{
    /// <summary>
    /// Turns a repository's <c>INSERT</c> into one that overwrites the row with the same id, so a table's
    /// columns are listed once, in the insert, for both.
    /// </summary>
    public static partial class UpsertSql
    {
        /// <summary>
        /// <paramref name="insert"/>, an <c>INSERT INTO table (id, …) VALUES (…)</c>, followed by an
        /// <c>ON CONFLICT (id)</c> clause that sets every other column it names to the inserted value.
        /// </summary>
        public static string OnIdConflict(string insert)
        {
            Match match = InsertColumns().Match(insert);
            if (!match.Success)
            {
                throw new ArgumentException("Not an INSERT INTO table (columns) statement.", nameof(insert));
            }

            IEnumerable<string> assignments = match.Groups["columns"].Value
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(column => column != "id")
                .Select(column => $"{column} = excluded.{column}");

            return $"{insert} ON CONFLICT (id) DO UPDATE SET {string.Join(", ", assignments)}";
        }

        [GeneratedRegex(@"INSERT\s+INTO\s+\w+\s*\((?<columns>[^)]*)\)", RegexOptions.IgnoreCase)]
        private static partial Regex InsertColumns();
    }
}
