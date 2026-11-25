namespace ByteShelf.Server.Replication.Services
{
    /// <summary>
    /// The primary logged a kind of change this build does not know, being newer. Thrown rather than
    /// skipped, so replication stops and says to update instead of silently dropping what the change held.
    /// </summary>
    public sealed class ReplicaOutdatedException : Exception
    {
        /// <param name="unknown">What this build does not know, as "a change to 'gadget'".</param>
        public ReplicaOutdatedException(string unknown)
            : base($"The primary logged {unknown}, which this server does not know; update it to the primary's version.")
        {
        }
    }
}
