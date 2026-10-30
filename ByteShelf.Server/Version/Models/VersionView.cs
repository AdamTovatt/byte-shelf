namespace ByteShelf.Server.Version.Models
{
    /// <summary>
    /// What the frontend polls to detect a new deploy. <see cref="VersionId"/> is a stable
    /// per-build identifier (the entry assembly's <c>ModuleVersionId</c>) — different across
    /// builds, identical within one running deployment. The frontend captures the value at app
    /// boot and shows a "new version available" banner when a later poll returns something else.
    /// </summary>
    public class VersionView
    {
        public required string VersionId { get; init; }
    }
}
