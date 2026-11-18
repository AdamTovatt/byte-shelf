namespace ByteShelfCommon
{
    /// <summary>What one item of a batch came to (<see cref="EntryBatch"/>): done, or refused with <see cref="Error"/>.</summary>
    public interface IEntryBatchResult
    {
        /// <summary>Why the item was refused, with its code; null when it was done.</summary>
        ErrorResponse? Error { get; }
    }
}
