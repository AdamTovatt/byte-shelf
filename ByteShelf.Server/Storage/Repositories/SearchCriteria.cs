using ByteShelfCommon;
using EasyReasy.Database.Mapping;

namespace ByteShelf.Server.Storage.Repositories
{
    /// <summary>
    /// The normalized <see cref="FileCriteria"/> but where a file is: <see cref="Sql"/> is the condition
    /// a row of <c>file</c> meets when it matches every one, shared by search and the search for
    /// duplicates. A query that uses it adds <see cref="AddTo"/>'s parameters.
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
        string? Sha256)
    {
        public const string Sql = $@"
            (@{nameof(Text)} IS NULL
             OR instr(file.name_fold, @{nameof(Text)}) > 0
             OR instr(coalesce(file.description_fold, ''), @{nameof(Text)}) > 0)
            AND (@{nameof(MediaType)} IS NULL OR file.media_type = @{nameof(MediaType)})
            AND (@{nameof(MediaPrefix)} IS NULL
                 OR substr(file.media_type, 1, length(@{nameof(MediaPrefix)})) = @{nameof(MediaPrefix)})
            AND (@{nameof(MinSize)} IS NULL OR file.size_bytes >= @{nameof(MinSize)})
            AND (@{nameof(MaxSize)} IS NULL OR file.size_bytes <= @{nameof(MaxSize)})
            AND (@{nameof(CreatedFrom)} IS NULL OR file.created_at >= @{nameof(CreatedFrom)})
            AND (@{nameof(CreatedBefore)} IS NULL OR file.created_at < @{nameof(CreatedBefore)})
            AND (@{nameof(UpdatedFrom)} IS NULL OR file.updated_at >= @{nameof(UpdatedFrom)})
            AND (@{nameof(UpdatedBefore)} IS NULL OR file.updated_at < @{nameof(UpdatedBefore)})
            AND (@{nameof(Sha256)} IS NULL OR file.sha256 = @{nameof(Sha256)})";

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
                search.Sha256);
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
        }
    }
}
