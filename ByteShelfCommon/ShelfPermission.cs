using System.Text.Json.Serialization;
using ByteShelfCommon.Serialization;

namespace ByteShelfCommon
{
    /// <summary>What whoever asks, a user or a program with a key, may do with a shelf's files.</summary>
    [JsonConverter(typeof(CamelCaseEnumConverter<ShelfPermission>))]
    public enum ShelfPermission
    {
        /// <summary>List, read and download.</summary>
        Read,

        /// <summary>Read, and store new files, but never overwrite, move, delete or restore anything. Only a key's.</summary>
        AddOnly,

        /// <summary>Everything a shelf's files allow: also upload, overwrite, move, delete and restore.</summary>
        ReadWrite,
    }
}
