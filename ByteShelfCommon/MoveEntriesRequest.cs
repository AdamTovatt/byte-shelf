namespace ByteShelfCommon
{
    /// <summary>
    /// Moves or renames files and directories, as <see cref="EntryBatch"/> applies a batch: either
    /// <see cref="Moves"/>, each with its own new path, or <see cref="Paths"/> into one directory,
    /// <see cref="IntoDirectory"/>, keeping their names, as <c>mv a b c dir/</c> does. Never overwrites;
    /// missing directories above a new path are created.
    /// </summary>
    public sealed class MoveEntriesRequest
    {
        /// <summary>Each move, in order; leave <see cref="Paths"/> and <see cref="IntoDirectory"/> out with these.</summary>
        public IReadOnlyList<EntryMove>? Moves { get; init; }

        /// <summary>The files and directories to move into <see cref="IntoDirectory"/>, in order.</summary>
        public IReadOnlyList<string>? Paths { get; init; }

        /// <summary>The directory <see cref="Paths"/> move into, each keeping its name.</summary>
        public string? IntoDirectory { get; init; }

        /// <summary>
        /// The moves in order, every path normalized: <see cref="Moves"/>, or one move per path in
        /// <see cref="Paths"/> to its name inside <see cref="IntoDirectory"/>. A new path that is too long
        /// once joined is left for the move to refuse, as that item's own refusal.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Neither form or both is given, there are not 1 to <see cref="EntryBatch.MaxItems"/> items, a path
        /// is not allowed, or the root is among <see cref="Paths"/>, which has no name to keep.
        /// </exception>
        public IReadOnlyList<EntryMove> Normalize()
        {
            if (Moves != null)
            {
                if (Paths != null || IntoDirectory != null)
                {
                    throw new ArgumentException("Give moves, or paths with intoDirectory, not both.");
                }

                return EntryBatch.Check(Moves, "moves")
                    .Select(move => new EntryMove { Path = ShelfPath.Normalize(move.Path), ToPath = ShelfPath.Normalize(move.ToPath) })
                    .ToList();
            }

            if (Paths == null || IntoDirectory == null)
            {
                throw new ArgumentException("Give moves, or paths with intoDirectory.");
            }

            string directory = ShelfPath.Normalize(IntoDirectory);
            return EntryBatch.Check(Paths, "paths").Select(path => Into(directory, ShelfPath.Normalize(path))).ToList();
        }

        private static EntryMove Into(string directory, string path)
        {
            if (path == ShelfPath.Root)
            {
                throw new ArgumentException("The root directory cannot be moved.");
            }

            return new EntryMove { Path = path, ToPath = ShelfPath.Combine(directory, ShelfPath.GetName(path)) };
        }
    }
}
