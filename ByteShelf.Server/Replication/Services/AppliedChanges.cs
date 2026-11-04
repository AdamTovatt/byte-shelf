namespace ByteShelf.Server.Replication.Services
{
    /// <summary>
    /// What applying entries leaves to do once they commit, noted as <see cref="ReplicaApplier"/> applies
    /// them and done by <see cref="ReplicaSync"/>.
    /// </summary>
    public sealed class AppliedChanges
    {
        private readonly List<Guid> _removedUsers = new List<Guid>();
        private readonly HashSet<string> _releasedObjects = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The users removed, whose sessions end.</summary>
        public IReadOnlyCollection<Guid> RemovedUsers => _removedUsers;

        /// <summary>Objects files pointed at before their contents were swapped, to remove unless a row still points at one.</summary>
        public IReadOnlyCollection<string> ReleasedObjects => _releasedObjects;

        public void UserRemoved(Guid userId)
        {
            _removedUsers.Add(userId);
        }

        public void ObjectReleased(string sha256)
        {
            _releasedObjects.Add(sha256);
        }
    }
}
