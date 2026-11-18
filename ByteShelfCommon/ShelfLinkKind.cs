using System.Text.Json.Serialization;
using ByteShelfCommon.Serialization;

namespace ByteShelfCommon
{
    /// <summary>What a link does.</summary>
    [JsonConverter(typeof(CamelCaseEnumConverter<ShelfLinkKind>))]
    public enum ShelfLinkKind
    {
        /// <summary>Reads one file: a <see cref="DownloadLink"/>, made to share it.</summary>
        Download,

        /// <summary>Stores one file at a path: an <see cref="UploadLink"/>.</summary>
        Upload,
    }
}
