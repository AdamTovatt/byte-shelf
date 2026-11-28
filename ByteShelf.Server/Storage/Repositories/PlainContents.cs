using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Shared.Database;

namespace ByteShelf.Server.Storage.Repositories
{
    /// <summary>
    /// Which contents the server may hold something read from: those a plain file holds. The conditions
    /// every table of readings shares, so none is kept, recorded or read by a rule of its own.
    /// </summary>
    internal static class PlainContents
    {
        /// <summary>
        /// The condition that a live plain file holds the contents <paramref name="sha256"/>, a column or
        /// parameter. A reading is recorded only where it is true in the same transaction, since they may
        /// have been encrypted or removed while they were read.
        /// </summary>
        private static string HeldByLiveFile(string sha256)
        {
            return $"EXISTS (SELECT 1 FROM file WHERE file.sha256 = {sha256} AND file.encrypted = 0 AND file.deleted_at IS NULL)";
        }

        /// <summary>
        /// The condition that a plain file, live or in the trash (for a restore), holds the contents <paramref name="sha256"/>,
        /// a column or parameter; but for the file <paramref name="unless"/> names, when it names one.
        /// </summary>
        public static string HeldByFile(string sha256, string? unless = null)
        {
            string other = unless == null ? string.Empty : $" AND file.id <> {unless}";
            return $"EXISTS (SELECT 1 FROM file WHERE file.sha256 = {sha256} AND file.encrypted = 0{other})";
        }

        /// <summary>
        /// Records a reading of the contents <c>@Sha256</c> in <paramref name="table"/>, by an insert of the columns and
        /// values given, over what another version of the reader recorded: nothing when no live plain file holds
        /// them, or this version has.
        /// </summary>
        public static string RecordSql(string table, string columns, string values)
        {
            string upsert = UpsertSql.OnIdConflict($"INSERT INTO {table} ({columns}) SELECT {values} WHERE {HeldByLiveFile("@Sha256")}");
            return $"{upsert} WHERE {table}.reader_version <> excluded.reader_version";
        }

        /// <summary>
        /// Deletes the rows of <paramref name="table"/>, one of readings, whose contents no plain file holds,
        /// live or in the trash (for a restore); answers their ids.
        /// </summary>
        public static string DeleteUnheldSql(string table)
        {
            return $@"
                DELETE FROM {table}
                WHERE NOT {HeldByFile($"{table}.sha256")}
                RETURNING id";
        }

        /// <summary>Deletes the rows of <paramref name="table"/> of the contents <c>@sha256</c>, unless a plain file other than <c>@fileId</c> holds them; answers their ids.</summary>
        public static string DeleteUnheldByOthersSql(string table)
        {
            return $@"
                DELETE FROM {table}
                WHERE {table}.sha256 = @sha256
                  AND NOT {HeldByFile("@sha256", unless: "@fileId")}
                RETURNING id";
        }

        /// <summary>
        /// The SHA-256 of contents live plain files of <paramref name="mediaTypes"/> hold, each once and in
        /// order, after <c>@after</c> and at most <c>@limit</c>.
        /// </summary>
        public static string LiveSql(MediaTypeSet mediaTypes)
        {
            return LiveSql(mediaTypes, unless: null);
        }

        /// <summary>As <see cref="LiveSql(MediaTypeSet)"/>, but those <paramref name="read"/> is true of: a condition on <c>file.sha256</c>.</summary>
        public static string UnreadSql(MediaTypeSet mediaTypes, string read)
        {
            return LiveSql(mediaTypes, unless: read);
        }

        /// <summary>The condition that the row of <c>file</c> is a live plain file of <paramref name="mediaTypes"/>.</summary>
        public static string IsLiveSql(MediaTypeSet mediaTypes)
        {
            return $"file.deleted_at IS NULL AND file.encrypted = 0 AND {OneOf(mediaTypes, "file.media_type")}";
        }

        private static string LiveSql(MediaTypeSet mediaTypes, string? unless)
        {
            return $@"
                SELECT DISTINCT file.sha256
                FROM file
                WHERE {IsLiveSql(mediaTypes)}
                  {(unless == null ? string.Empty : $"AND NOT ({unless})")}
                  AND (@after IS NULL OR file.sha256 > @after)
                ORDER BY file.sha256
                LIMIT @limit";
        }

        /// <summary>
        /// The condition that <paramref name="column"/>, a media type, is one of <paramref name="mediaTypes"/>. The types
        /// are written into it rather than bound: <see cref="MediaTypeSet.Of"/> takes no character a string literal could
        /// end with.
        /// </summary>
        public static string OneOf(MediaTypeSet mediaTypes, string column)
        {
            IEnumerable<string> conditions = mediaTypes.Patterns.Select(pattern => pattern.Prefix == null
                ? $"{column} = '{pattern.Type}'"
                : $"substr({column}, 1, {pattern.Prefix.Length}) = '{pattern.Prefix}'");

            return $"({string.Join(" OR ", conditions.DefaultIfEmpty("0"))})";
        }
    }
}
