using ByteShelfClient;

namespace ByteShelf.Cli.Connection
{
    /// <summary>Where commands get their client, and how signing in and out changes it.</summary>
    internal interface IClientSource
    {
        /// <summary>
        /// The client for the login in effect now: the same one while the login stays the same, a new
        /// one after a login or logout made elsewhere.
        /// </summary>
        /// <exception cref="UsageException">Nothing says which server or credentials to use.</exception>
        Task<IByteShelfClient> OpenAsync(CancellationToken cancellationToken);

        /// <summary>Signs a user in and keeps the session for later commands.</summary>
        Task SignInAsync(string server, string email, string password, CancellationToken cancellationToken);

        /// <summary>Checks a shelf key against the server and keeps it for later commands.</summary>
        Task UseKeyAsync(string server, string key, CancellationToken cancellationToken);

        /// <summary>Ends a kept session on the server, as far as it can, and forgets what was kept.</summary>
        Task SignOutAsync(CancellationToken cancellationToken);

        /// <summary>The server and how commands prove who they are there, or null when nothing is set up.</summary>
        string? Describe();
    }
}
