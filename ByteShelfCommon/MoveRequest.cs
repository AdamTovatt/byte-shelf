namespace ByteShelfCommon
{
    /// <summary>
    /// Moves a file or directory to a new path, which renames it when only the last name differs.
    /// The stored contents never move; missing directories above the new path are created.
    /// </summary>
    public sealed class MoveRequest
    {
        /// <summary>The full path to move to. Missing directories above it are created; the path itself must be free and cannot be the root.</summary>
        public required string ToPath { get; init; }
    }
}
