namespace ByteShelf.Server.Shared.Errors
{
    /// <summary>A write sent to a replica, which takes changes only from its primary. Answered as 409.</summary>
    public class ReadOnlyReplicaException : Exception
    {
        public ReadOnlyReplicaException()
            : base("This server is a replica and only reads; make the change on its primary.")
        {
        }
    }
}
