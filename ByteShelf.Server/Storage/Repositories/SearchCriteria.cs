using ByteShelfCommon;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Storage.Repositories
{
    /// <summary>
    /// The normalized <see cref="FileCriteria"/> but where a file is: <see cref="Sql"/> is the condition
    /// a row of <c>file</c>, joined to what was read from its contents by <see cref="Join"/>, meets when
    /// it matches every one, shared by search and the search for duplicates. A query that uses it adds
    /// <see cref="AddTo"/>'s parameters.
    /// </summary>
    internal sealed record SearchCriteria(
        string? Text,
        string? MediaType,
        string? MediaPrefix,
        long? MinSize,
        long? MaxSize,
        DateTimeOffset? CreatedFrom,
        DateTimeOffset? CreatedBefore,
        DateTimeOffset? UpdatedFrom,
        DateTimeOffset? UpdatedBefore,
        string? Sha256,
        bool? HasDescription,
        DateTimeOffset? TakenFrom,
        DateTimeOffset? TakenBefore,
        bool HasGps,
        double? MinDuration,
        double? MaxDuration,
        int? MinPages,
        int? MaxPages)
    {
        /// <summary>
        /// Each file with what was read from its contents (<c>object_metadata</c>, see Metadata.md) as
        /// <c>meta</c>, none for an encrypted file, whose contents are never read, so no metadata
        /// criterion matches it. Only the columns <see cref="Sql"/> compares are taken, under names no
        /// <c>file</c> column has, so a query's unqualified columns stay the file's.
        /// </summary>
        public const string Join = @"LEFT JOIN (
                SELECT sha256 AS object_sha256, taken_at, latitude, duration_seconds, page_count, title_fold, camera_fold
                FROM object_metadata) meta
              ON meta.object_sha256 = file.sha256 AND file.encrypted = 0";

        /// <summary>
        /// Every criterion that is set. The text matches the title and camera read from the contents as
        /// it does a name. A file without metadata fails every metadata criterion, since a comparison
        /// with null is never true. Ranges as in <see cref="FileCriteria"/>: a time includes its start and
        /// excludes its end; sizes, durations and page counts are inclusive.
        /// </summary>
        public const string Sql = $@"
            (@{nameof(Text)} IS NULL
             OR instr(file.name_fold, @{nameof(Text)}) > 0
             OR instr(coalesce(file.description_fold, ''), @{nameof(Text)}) > 0
             OR instr(coalesce(meta.title_fold, ''), @{nameof(Text)}) > 0
             OR instr(coalesce(meta.camera_fold, ''), @{nameof(Text)}) > 0)
            AND (@{nameof(MediaType)} IS NULL OR file.media_type = @{nameof(MediaType)})
            AND (@{nameof(MediaPrefix)} IS NULL
                 OR substr(file.media_type, 1, length(@{nameof(MediaPrefix)})) = @{nameof(MediaPrefix)})
            AND (@{nameof(MinSize)} IS NULL OR file.size_bytes >= @{nameof(MinSize)})
            AND (@{nameof(MaxSize)} IS NULL OR file.size_bytes <= @{nameof(MaxSize)})
            AND (@{nameof(CreatedFrom)} IS NULL OR file.created_at >= @{nameof(CreatedFrom)})
            AND (@{nameof(CreatedBefore)} IS NULL OR file.created_at < @{nameof(CreatedBefore)})
            AND (@{nameof(UpdatedFrom)} IS NULL OR file.updated_at >= @{nameof(UpdatedFrom)})
            AND (@{nameof(UpdatedBefore)} IS NULL OR file.updated_at < @{nameof(UpdatedBefore)})
            AND (@{nameof(Sha256)} IS NULL OR file.sha256 = @{nameof(Sha256)})
            AND (@{nameof(HasDescription)} IS NULL OR (file.description IS NOT NULL) = @{nameof(HasDescription)})
            AND (@{nameof(TakenFrom)} IS NULL OR meta.taken_at >= @{nameof(TakenFrom)})
            AND (@{nameof(TakenBefore)} IS NULL OR meta.taken_at < @{nameof(TakenBefore)})
            AND (NOT @{nameof(HasGps)} OR meta.latitude IS NOT NULL)
            AND (@{nameof(MinDuration)} IS NULL OR meta.duration_seconds >= @{nameof(MinDuration)})
            AND (@{nameof(MaxDuration)} IS NULL OR meta.duration_seconds <= @{nameof(MaxDuration)})
            AND (@{nameof(MinPages)} IS NULL OR meta.page_count >= @{nameof(MinPages)})
            AND (@{nameof(MaxPages)} IS NULL OR meta.page_count <= @{nameof(MaxPages)})";

        /// <summary>The criteria of a normalized search.</summary>
        public static SearchCriteria Of(FileCriteria search)
        {
            return new SearchCriteria(
                search.Text,
                search.ExactMediaType(),
                search.MediaTypePrefix(),
                search.MinSizeBytes,
                search.MaxSizeBytes,
                search.CreatedFrom,
                search.CreatedBefore,
                search.UpdatedFrom,
                search.UpdatedBefore,
                search.Sha256,
                search.HasDescription,
                search.TakenFrom,
                search.TakenBefore,
                search.HasGps,
                search.MinDurationSeconds,
                search.MaxDurationSeconds,
                search.MinPageCount,
                search.MaxPageCount);
        }

        /// <summary>Adds the criteria to <paramref name="parameters"/>, under the names <see cref="Sql"/> uses.</summary>
        public void AddTo(DynamicParameters parameters)
        {
            parameters.Add(nameof(Text), Text);
            parameters.Add(nameof(MediaType), MediaType);
            parameters.Add(nameof(MediaPrefix), MediaPrefix);
            parameters.Add(nameof(MinSize), MinSize);
            parameters.Add(nameof(MaxSize), MaxSize);
            parameters.Add(nameof(CreatedFrom), CreatedFrom);
            parameters.Add(nameof(CreatedBefore), CreatedBefore);
            parameters.Add(nameof(UpdatedFrom), UpdatedFrom);
            parameters.Add(nameof(UpdatedBefore), UpdatedBefore);
            parameters.Add(nameof(Sha256), Sha256);
            parameters.Add(nameof(HasDescription), HasDescription);
            parameters.Add(nameof(TakenFrom), TakenFrom);
            parameters.Add(nameof(TakenBefore), TakenBefore);
            parameters.Add(nameof(HasGps), HasGps);
            parameters.Add(nameof(MinDuration), MinDuration);
            parameters.Add(nameof(MaxDuration), MaxDuration);
            parameters.Add(nameof(MinPages), MinPages);
            parameters.Add(nameof(MaxPages), MaxPages);
        }
    }
}
