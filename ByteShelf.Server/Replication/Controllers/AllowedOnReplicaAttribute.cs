namespace ByteShelf.Server.Replication.Controllers
{
    /// <summary>
    /// Marks an action that is not a GET but changes nothing a replica holds, such as asking for a
    /// download link, or that only a replica does, such as promoting it. Every other such action is
    /// refused on a replica by <see cref="ReplicaReadOnlyFilter"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AllowedOnReplicaAttribute : Attribute
    {
    }
}
