using System.Text.Json.Serialization;
using ByteShelfCommon.Serialization;

namespace ByteShelfCommon
{
    /// <summary>What a user may do on a shelf they have been given access to.</summary>
    [JsonConverter(typeof(CamelCaseEnumConverter<ShelfAccessPermission>))]
    public enum ShelfAccessPermission
    {
        /// <summary>List, read and download.</summary>
        Read,

        /// <summary>Everything a shelf's files allow: also upload, overwrite, move, delete and restore.</summary>
        ReadWrite,
    }
}
