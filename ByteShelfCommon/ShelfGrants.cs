namespace ByteShelfCommon
{
    /// <summary>The rules for granting a user access to a shelf.</summary>
    public static class ShelfGrants
    {
        /// <summary>Refuses a permission that is none of <see cref="ShelfAccessPermission"/>'s values.</summary>
        /// <exception cref="ArgumentException"><paramref name="permission"/> is none of the values.</exception>
        public static void ValidatePermission(ShelfAccessPermission permission)
        {
            // Not shared with ShelfKeys.ValidatePermission through a generic helper: each message names its own permission.
            if (!Enum.IsDefined(permission))
            {
                throw new ArgumentException($"'{permission}' is not a shelf access permission.", nameof(permission));
            }
        }
    }
}
