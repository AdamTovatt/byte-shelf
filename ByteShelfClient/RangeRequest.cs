namespace ByteShelfClient
{
    /// <summary>The rules for asking for a range, which <see cref="IShelfClient.OpenReadRangeAsync"/> applies in every client.</summary>
    internal static class RangeRequest
    {
        /// <summary>Checks a range asked for, before anything is sent.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The offset is negative, or the length is not positive.</exception>
        public static void Check(long offset, long? length)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            if (length != null)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length.Value, nameof(length));
            }
        }

        /// <summary>
        /// Whether a range asked for is the whole file whatever its length, which is read as a plain read, so
        /// an empty file answers an empty range rather than a refusal.
        /// </summary>
        public static bool AsksForAll(long offset, long? length)
        {
            return offset == 0 && length == null;
        }
    }
}
