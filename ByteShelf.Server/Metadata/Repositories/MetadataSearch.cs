using ByteShelfCommon;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Metadata.Repositories
{
    /// <summary>
    /// The part of a file search that asks what files' contents carry (<c>object_metadata</c>, see
    /// Metadata.md): the join, the title and camera the text matches, and the metadata criteria of
    /// <see cref="FileSearch"/>, with the parameters it binds them under.
    /// </summary>
    internal static class MetadataSearch
    {
        /// <summary>
        /// Each file with what was read from its contents as <c>meta</c>, none for an encrypted file, whose
        /// contents are never read, so no metadata criterion matches it. Only the columns search compares
        /// are taken, under names no <c>file</c> column has, so the query's unqualified columns stay the
        /// file's.
        /// </summary>
        public const string Join = @"LEFT JOIN (
                        SELECT sha256 AS object_sha256, taken_at, latitude, duration_seconds, page_count, title_fold, camera_fold
                        FROM object_metadata) meta
                      ON meta.object_sha256 = file.sha256 AND file.encrypted = 0";

        /// <summary>
        /// Every metadata criterion that is set, bound by <see cref="AddParameters"/>. A file without
        /// metadata fails each one, since a comparison with null is never true. Ranges as in
        /// <see cref="FileSearch"/>: a time includes its start and excludes its end, durations and page
        /// counts are inclusive.
        /// </summary>
        public const string Conditions = @"(@meta_taken_from IS NULL OR meta.taken_at >= @meta_taken_from)
                      AND (@meta_taken_before IS NULL OR meta.taken_at < @meta_taken_before)
                      AND (NOT @meta_has_gps OR meta.latitude IS NOT NULL)
                      AND (@meta_min_duration IS NULL OR meta.duration_seconds >= @meta_min_duration)
                      AND (@meta_max_duration IS NULL OR meta.duration_seconds <= @meta_max_duration)
                      AND (@meta_min_pages IS NULL OR meta.page_count >= @meta_min_pages)
                      AND (@meta_max_pages IS NULL OR meta.page_count <= @meta_max_pages)";

        /// <summary>Whether the folded text, bound as <paramref name="textParameter"/>, is in the title or camera, as in a name.</summary>
        public static string TextCondition(string textParameter)
        {
            return $"instr(coalesce(meta.title_fold, ''), @{textParameter}) > 0 OR instr(coalesce(meta.camera_fold, ''), @{textParameter}) > 0";
        }

        /// <summary>Binds <paramref name="search"/>'s metadata criteria under the names <see cref="Conditions"/> uses.</summary>
        public static void AddParameters(DynamicParameters parameters, FileSearch search)
        {
            parameters.Add("meta_taken_from", search.TakenFrom);
            parameters.Add("meta_taken_before", search.TakenBefore);
            parameters.Add("meta_has_gps", search.HasGps);
            parameters.Add("meta_min_duration", search.MinDurationSeconds);
            parameters.Add("meta_max_duration", search.MaxDurationSeconds);
            parameters.Add("meta_min_pages", search.MinPageCount);
            parameters.Add("meta_max_pages", search.MaxPageCount);
        }
    }
}
