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

        /// <summary>The condition that a plain file, live or in the trash (for a restore), holds the contents <paramref name="sha256"/>, a column or parameter.</summary>
        public static string HeldByFile(string sha256)
        {
            return $"EXISTS (SELECT 1 FROM file WHERE file.sha256 = {sha256} AND file.encrypted = 0)";
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
                  AND NOT EXISTS (SELECT 1 FROM file WHERE file.sha256 = @sha256 AND file.encrypted = 0 AND file.id <> @fileId)
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

        private static string LiveSql(MediaTypeSet mediaTypes, string? unless)
        {
            return $@"
                SELECT DISTINCT file.sha256
                FROM file
                WHERE file.deleted_at IS NULL
                  AND file.encrypted = 0
                  AND {mediaTypes.Sql("file.media_type")}
                  {(unless == null ? string.Empty : $"AND NOT ({unless})")}
                  AND (@after IS NULL OR file.sha256 > @after)
                ORDER BY file.sha256
                LIMIT @limit";
        }
    }
}
