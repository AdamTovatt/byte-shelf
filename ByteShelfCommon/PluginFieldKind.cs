using System.Text.Json.Serialization;
using ByteShelfCommon.Serialization;

namespace ByteShelfCommon
{
    /// <summary>What a plugin's declared field holds.</summary>
    [JsonConverter(typeof(CamelCaseEnumConverter<PluginFieldKind>))]
    public enum PluginFieldKind
    {
        /// <summary>A JSON string.</summary>
        Text,

        /// <summary>A JSON number.</summary>
        Number,

        /// <summary>An instant: a JSON string of an ISO 8601 date and time with an offset, such as <c>2026-03-01T14:30:00Z</c>.</summary>
        Date,

        /// <summary>A JSON boolean.</summary>
        Flag,
    }
}
