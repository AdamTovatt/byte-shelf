using System.Text.Json.Serialization;
using ByteShelfCommon.Serialization;

namespace ByteShelfCommon
{
    /// <summary>
    /// What a plugin can make from a file's contents, for the server to keep and serve beside them. A server may have
    /// kinds a client does not know yet, which <see cref="ShelfFile.Derived"/> leaves out as it is read.
    /// </summary>
    [JsonConverter(typeof(CamelCaseEnumConverter<DerivedKind>))]
    public enum DerivedKind
    {
        /// <summary>A small picture for listings, made after an upload.</summary>
        Thumbnail,

        /// <summary>What a viewer shows until it has the contents themselves, made after an upload.</summary>
        Preview,

        /// <summary>The whole contents in a type a browser shows, made when asked for.</summary>
        Full,

        /// <summary>What an agent is shown of the contents, a PNG, JPEG, GIF or WebP of at most 1 MiB, made when asked for.</summary>
        Agent,

        /// <summary>
        /// The contents remade for streaming, in a type a browser plays and seeks, made in the background after an upload
        /// and never when asked for, as making one may take hours.
        /// </summary>
        Stream,
    }
}
