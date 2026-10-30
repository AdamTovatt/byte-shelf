namespace ByteShelf.Server.Invalidation.Models
{
    /// <summary>
    /// The resource vocabulary published through <see cref="Services.IInvalidationBroadcaster"/>.
    /// The web UI maps each constant to the queries it refreshes, so every constant here needs an
    /// entry there.
    /// </summary>
    public static class InvalidationResources
    {
        public const string Users = "users";
    }
}
