namespace ByteShelfCommon
{
    /// <summary>The rules for what a shelf is created or updated with: its name and its quota.</summary>
    public static class ShelfSettings
    {
        /// <summary>Throws unless <paramref name="name"/> passes <see cref="ShelfName"/> and the quota, when set, is not negative.</summary>
        /// <exception cref="ArgumentException">One of them is not allowed.</exception>
        public static void Validate(string name, long? quotaBytes)
        {
            ShelfName.Validate(name);
            if (quotaBytes < 0)
            {
                throw new ArgumentException("A quota cannot be negative.", nameof(quotaBytes));
            }
        }
    }
}
