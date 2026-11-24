using System.Text.Json.Serialization;
using ByteShelfCommon.Serialization;

namespace ByteShelfCommon
{
    /// <summary>What a plugin can make from a file's contents, for the server to keep and serve beside them.</summary>
    [JsonConverter(typeof(CamelCaseEnumConverter<DerivedKind>))]
    public enum DerivedKind
    {
        /// <summary>A small picture for listings, made after an upload.</summary>
        Thumbnail,

        /// <summary>What a viewer shows until it has the contents themselves, made after an upload.</summary>
        Preview,

        /// <summary>The whole contents in a type a browser shows, made when asked for.</summary>
        Full,

        /// <summary>What an agent is shown of the contents, made when asked for.</summary>
        Agent,
    }
}
