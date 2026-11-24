namespace ByteShelf.Server.Replication.Services
{
    /// <summary>
    /// What applying entries leaves to do, noted as <see cref="ReplicaApplier"/> applies them: after the last
    /// one, in their transaction, by <see cref="ReplicaApplier.SettleAsync"/>, and once they commit, by
    /// <see cref="ReplicaSync"/>.
    /// </summary>
    public sealed class AppliedChanges
    {
        private readonly List<Guid> _removedUsers = new List<Guid>();
        private readonly HashSet<string> _releasedObjects = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The users removed, whose sessions end once they commit.</summary>
        public IReadOnlyCollection<Guid> RemovedUsers => _removedUsers;

        /// <summary>Objects files pointed at before their contents were swapped, to remove once they commit unless a row still points at one.</summary>
        public IReadOnlyCollection<string> ReleasedObjects => _releasedObjects;

        /// <summary>Whether a plugin's document about a file was written, whose file may not have come by the last entry.</summary>
        public bool DocumentsMayLackFiles { get; private set; }

        public void UserRemoved(Guid userId)
        {
            _removedUsers.Add(userId);
        }

        public void ObjectReleased(string sha256)
        {
            _releasedObjects.Add(sha256);
        }

        public void FileDocumentWritten()
        {
            DocumentsMayLackFiles = true;
        }
    }
}
