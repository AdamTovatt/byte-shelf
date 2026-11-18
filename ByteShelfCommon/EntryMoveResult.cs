namespace ByteShelfCommon
{
    /// <summary>
    /// What one move of a <see cref="MoveEntriesRequest"/> came to: done, with <see cref="File"/> or
    /// <see cref="Directory"/> where it is now, or refused with <see cref="Error"/>.
    /// </summary>
    public sealed class EntryMoveResult : IEntryBatchResult
    {
        /// <summary>Where the entry was, normalized.</summary>
        public required string Path { get; init; }

        /// <summary>Where it was to go, normalized.</summary>
        public required string ToPath { get; init; }

        /// <summary>The file moved, as it is now; null when a directory moved or the move was refused.</summary>
        public ShelfFile? File { get; init; }

        /// <summary>The directory moved, as it is now; null when a file moved or the move was refused.</summary>
        public ShelfDirectory? Directory { get; init; }

        /// <inheritdoc />
        public ErrorResponse? Error { get; init; }

        /// <summary><paramref name="move"/> done, moving <paramref name="file"/>.</summary>
        public static EntryMoveResult Moved(EntryMove move, ShelfFile file)
        {
            return new EntryMoveResult { Path = move.Path, ToPath = move.ToPath, File = file };
        }

        /// <summary><paramref name="move"/> done, moving <paramref name="directory"/>.</summary>
        public static EntryMoveResult Moved(EntryMove move, ShelfDirectory directory)
        {
            return new EntryMoveResult { Path = move.Path, ToPath = move.ToPath, Directory = directory };
        }

        /// <summary><paramref name="move"/> refused, for <paramref name="error"/>.</summary>
        public static EntryMoveResult Refused(EntryMove move, ErrorResponse error)
        {
            return new EntryMoveResult { Path = move.Path, ToPath = move.ToPath, Error = error };
        }
    }
}
