namespace ByteShelfCommon
{
    /// <summary>
    /// Which release a ByteShelf server runs, answered only to a signed-in user or a shelf key: an
    /// anonymous caller who learned the release could match the server against its known bugs.
    /// </summary>
    /// <remarks>Named as the client call that answers it, <c>IByteShelfClient.GetServerVersionAsync</c>.</remarks>
    public sealed class ServerVersion
    {
        /// <summary>The release, as <see cref="ReleaseVersion.Of"/> reads it from the server, such as <c>2.3.0</c>.</summary>
        public required string Version { get; init; }

        /// <summary>
        /// The running deploy's id, which changes whenever the server's code or its web UI does; the
        /// same id the public <c>api/version/current</c> answers, given here too so one call says both
        /// which release runs and which deploy of it.
        /// </summary>
        public required string VersionId { get; init; }
    }
}
