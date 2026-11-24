using System.Text.Json;
using System.Text.Json.Serialization;

namespace ByteShelfCommon
{
    /// <summary>Writes a <see cref="PluginDocument"/> as the object it is, and reads one back by its rules.</summary>
    public sealed class PluginDocumentJsonConverter : JsonConverter<PluginDocument>
    {
        /// <inheritdoc />
        public override PluginDocument Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            try
            {
                return PluginDocument.Parse(document.RootElement.GetRawText());
            }
            catch (ArgumentException exception)
            {
                throw new JsonException(exception.Message, exception);
            }
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, PluginDocument value, JsonSerializerOptions options)
        {
            writer.WriteRawValue(value.Json, skipInputValidation: true);
        }
    }
}
