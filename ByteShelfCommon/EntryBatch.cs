namespace ByteShelfCommon
{
    /// <summary>
    /// The rules every batch of entries shares (<see cref="MoveEntriesRequest"/>,
    /// <see cref="SetDescriptionsRequest"/>): each item is applied on its own, in the order given, so a
    /// later item may use a path an earlier one made, and the answer holds each item's result in that
    /// order. Nothing is undone when an item is refused.
    /// </summary>
    public static class EntryBatch
    {
        /// <summary>The most items one batch holds.</summary>
        public const int MaxItems = 1000;

        /// <summary><paramref name="items"/> when it holds 1 to <see cref="MaxItems"/> items and none is null.</summary>
        /// <exception cref="ArgumentException">It is empty, longer, or holds a null.</exception>
        internal static IReadOnlyList<T> Check<T>(IReadOnlyList<T> items, string name)
        {
            if (items.Count == 0 || items.Count > MaxItems)
            {
                throw new ArgumentException($"Give 1 to {MaxItems} {name}; this gives {items.Count}.");
            }

            return items.Any(item => item == null) ? throw new ArgumentException($"One of the {name} is null.") : items;
        }
    }
}
