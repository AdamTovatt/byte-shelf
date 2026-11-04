using System.Text.Json.Serialization;
using ByteShelfCommon.Serialization;

namespace ByteShelfCommon
{
    /// <summary>What a program holding a shelf key may do on the key's shelf.</summary>
    [JsonConverter(typeof(CamelCaseEnumConverter<ShelfKeyPermission>))]
    public enum ShelfKeyPermission
    {
        /// <summary>List, read and download.</summary>
        Read,

        /// <summary>
        /// Read, and store new files, but never overwrite, move, delete or restore anything: a program
        /// whose key leaks cannot wipe what it stored, such as its own backups.
        /// </summary>
        AddOnly,

        /// <summary>Everything a shelf's files allow.</summary>
        ReadWrite,
    }
}
