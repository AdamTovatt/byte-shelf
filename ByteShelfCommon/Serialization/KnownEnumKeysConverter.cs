using System.Text.Json;
using System.Text.Json.Serialization;

namespace ByteShelfCommon.Serialization
{
    /// <summary>
    /// Reads texts by the names of <typeparamref name="TEnum"/>'s values, leaving out names this version does not know,
    /// whatever their values, so a value a later server adds never fails what holds it. Writes each by its camelCase name,
    /// and reads names case-insensitively, as <see cref="CamelCaseEnumConverter{TEnum}"/> does.
    /// </summary>
    /// <typeparam name="TEnum">The enum whose names are the keys.</typeparam>
    public sealed class KnownEnumKeysConverter<TEnum> : JsonConverter<IReadOnlyDictionary<TEnum, string>>
        where TEnum : struct, Enum
    {
        /// <summary>The values by their names; not numbers, nor lists of names.</summary>
        private static readonly Dictionary<string, TEnum> Values = Enum.GetValues<TEnum>().ToDictionary(NameOf, StringComparer.OrdinalIgnoreCase);

        /// <inheritdoc />
        public override IReadOnlyDictionary<TEnum, string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException($"The texts by {typeof(TEnum).Name} are not an object.");
            }

            Dictionary<TEnum, string> read = new Dictionary<TEnum, string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string name = reader.GetString()!;
                if (!reader.Read())
                {
                    throw new JsonException($"The {name} has no value.");
                }

                if (!Values.TryGetValue(name, out TEnum key))
                {
                    reader.Skip();
                    continue;
                }

                if (reader.TokenType != JsonTokenType.String)
                {
                    throw new JsonException($"The {name} is no text.");
                }

                read[key] = reader.GetString()!;
            }

            return read;
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<TEnum, string> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach ((TEnum key, string text) in value)
            {
                writer.WriteString(NameOf(key), text);
            }

            writer.WriteEndObject();
        }

        private static string NameOf(TEnum value)
        {
            return JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
        }
    }
}
