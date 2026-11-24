namespace ByteShelf.Server.Version.Models
{
    /// <summary>
    /// What the frontend polls to detect a new deploy. <see cref="VersionId"/> identifies the running
    /// deploy: a hash of the server assembly's <c>ModuleVersionId</c>, the web UI's <c>index.html</c> and
    /// the plugins' web UI files, so it changes when the server's code or the web UI does, a plugin's part
    /// in it included, and stays put otherwise. The
    /// frontend captures the value at app boot and shows a "new version available" banner when a later
    /// poll returns something else.
    /// </summary>
    public class VersionView
    {
        public required string VersionId { get; init; }
    }
}
