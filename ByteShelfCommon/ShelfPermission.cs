using System.Text.Json.Serialization;
using ByteShelfCommon.Serialization;

namespace ByteShelfCommon
{
    /// <summary>What a user, or a program holding a shelf key, may do with a shelf's files.</summary>
    [JsonConverter(typeof(CamelCaseEnumConverter<ShelfPermission>))]
    public enum ShelfPermission
    {
        /// <summary>List, read and download.</summary>
        Read,

        /// <summary>
        /// Only a key has it. Read, and store new files, but never overwrite, move, delete or restore
        /// anything: a program whose key leaks cannot wipe what it stored, such as its own backups.
        /// </summary>
        AddOnly,

        /// <summary>Everything a shelf's files allow: also upload, overwrite, move, delete and restore.</summary>
        ReadWrite,
    }
}
