using System.Text.Json;
using System.Text.Json.Serialization;

namespace ByteShelfCommon.Serialization
{
    /// <summary>
    /// Writes an enum as its camelCase name and reads it back case-insensitively. Set on the enum type,
    /// so the server and every client agree without each configuring a converter.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    public sealed class CamelCaseEnumConverter<TEnum> : JsonStringEnumConverter<TEnum>
        where TEnum : struct, Enum
    {
        /// <summary>Creates the converter.</summary>
        public CamelCaseEnumConverter()
            : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
        {
        }
    }
}
