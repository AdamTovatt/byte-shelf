namespace ByteShelf.Server.Storage.Models
{
    /// <summary>
    /// The room each shelf with a quota had as it was measured (<see cref="Repositories.IShelfRepository.MeasureRoomAsync"/>):
    /// its quota less what its file rows and uploads took, 0 or less when it was over.
    /// </summary>
    public sealed class MeasuredRoom
    {
        private readonly IReadOnlyDictionary<Guid, long> _free;

        public MeasuredRoom(IReadOnlyDictionary<Guid, long> free)
        {
            _free = free;
        }

        /// <summary>Whether no shelf had a quota, so any room asked of it is there.</summary>
        public bool IsUnbounded => _free.Count == 0;

        /// <summary>
        /// As <see cref="Repositories.IShelfRepository.HasRoomForMoreOfAsync"/> asks it, of the room measured: whether every
        /// shelf has room for <paramref name="moreBytesPerRow"/> more on each of its rows that hold the contents, by how
        /// many rows hold them on each (<paramref name="rowsByShelf"/>).
        /// </summary>
        public bool HasRoomForMoreOf(IReadOnlyDictionary<Guid, int> rowsByShelf, long moreBytesPerRow)
        {
            return rowsByShelf.All(rows => !_free.TryGetValue(rows.Key, out long free) || moreBytesPerRow * rows.Value <= free);
        }
    }
}
