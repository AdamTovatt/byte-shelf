namespace ByteShelfCommon
{
    /// <summary>
    /// What one change of a <see cref="SetDescriptionsRequest"/> came to: done, with the <see cref="File"/>
    /// as it is now, or refused with <see cref="Error"/>.
    /// </summary>
    public sealed class DescriptionResult : IEntryBatchResult
    {
        /// <summary>The file's path, normalized.</summary>
        public required string Path { get; init; }

        /// <summary>The file with its new description; null when the change was refused.</summary>
        public ShelfFile? File { get; init; }

        /// <inheritdoc />
        public ErrorResponse? Error { get; init; }

        /// <summary>A change done, leaving <paramref name="file"/>.</summary>
        public static DescriptionResult Described(ShelfFile file)
        {
            return new DescriptionResult { Path = file.Path, File = file };
        }

        /// <summary><paramref name="change"/> refused, for <paramref name="error"/>.</summary>
        public static DescriptionResult Refused(DescriptionChange change, ErrorResponse error)
        {
            return new DescriptionResult { Path = change.Path, Error = error };
        }
    }
}
