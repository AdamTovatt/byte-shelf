namespace ByteShelf.Server.Version.Models
{
    /// <summary>
    /// What the frontend polls to detect a new deploy. <see cref="VersionId"/> identifies the running
    /// deploy: a hash of the server assembly's <c>ModuleVersionId</c> and the web UI's <c>index.html</c>,
    /// so it changes when either the server's code or the web UI does, and stays put otherwise. The
    /// frontend captures the value at app boot and shows a "new version available" banner when a later
    /// poll returns something else.
    /// </summary>
    public class VersionView
    {
        public required string VersionId { get; init; }
    }
}
