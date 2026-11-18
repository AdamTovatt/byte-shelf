using System.Text.Json.Serialization;
using ByteShelfCommon.Serialization;

namespace ByteShelfCommon
{
    /// <summary>Whether a link was made by a signed-in user or by a program with a key.</summary>
    [JsonConverter(typeof(CamelCaseEnumConverter<LinkMakerKind>))]
    public enum LinkMakerKind
    {
        /// <summary>A signed-in user.</summary>
        User,

        /// <summary>A program with one of the shelf's keys.</summary>
        Key,
    }
}
