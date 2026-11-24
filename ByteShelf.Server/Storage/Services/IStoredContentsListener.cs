namespace ByteShelf.Server.Storage.Services
{
    /// <summary>Told of contents that a live plain file came to hold, once that has committed.</summary>
    public interface IStoredContentsListener
    {
        /// <summary>Returns at once, and does not throw: what is done with them is done after.</summary>
        void Stored(string sha256);
    }
}
