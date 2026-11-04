using System.Text.Json.Serialization;
using ByteShelfCommon.Serialization;

namespace ByteShelfCommon
{
    /// <summary>What a deletion removed.</summary>
    [JsonConverter(typeof(CamelCaseEnumConverter<TrashEntryKind>))]
    public enum TrashEntryKind
    {
        /// <summary>One file.</summary>
        File,

        /// <summary>A directory and everything that was in it.</summary>
        Directory,
    }
}
