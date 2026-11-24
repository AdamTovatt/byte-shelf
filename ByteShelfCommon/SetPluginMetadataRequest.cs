namespace ByteShelfCommon
{
    /// <summary>Writes a plugin's document about a file whole, or removes it.</summary>
    public sealed class SetPluginMetadataRequest
    {
        /// <summary>The whole document; null removes it, and leaving it out of the JSON is refused.</summary>
        public required PluginDocument? Document { get; init; }

        /// <summary>The version it replaces (<see cref="PluginFileDetails.FileVersion"/>), a removed one's included; null for a first write.</summary>
        public int? ExpectedVersion { get; init; }
    }
}
