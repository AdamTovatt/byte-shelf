using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ByteShelfCommon
{
    /// <summary>
    /// What a plugin keeps about a file or its contents: one JSON object, of at most <see cref="MaxBytes"/>.
    /// Every instance holds the rules, whether a client made it, a plugin's reader did, or it came back from
    /// the server. In JSON it is the object itself.
    /// </summary>
    [JsonConverter(typeof(PluginDocumentJsonConverter))]
    public sealed class PluginDocument : IEquatable<PluginDocument>
    {
        /// <summary>The most bytes of UTF-8 a document may hold, counted as <see cref="Json"/> has it: without whitespace, and its text as itself rather than as escapes.</summary>
        public const int MaxBytes = 64 * 1024;

        /// <summary>
        /// How deep a document's objects and arrays may nest, itself counted. Half of what JSON readers
        /// allow, so an answer or a change-log entry that carries one is read by any of them.
        /// </summary>
        public const int MaxDepth = 32;

        private static readonly JsonDocumentOptions ReadOptions = new JsonDocumentOptions { MaxDepth = MaxDepth };

        // Escapes the least a writer offers, which is still more than JSON needs (Unescaped).
        private static readonly JsonWriterOptions WriteOptions = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        private PluginDocument(string json)
        {
            Json = json;
        }

        /// <summary>The object as JSON, without whitespace and with no character escaped but those JSON requires.</summary>
        public string Json { get; }

        /// <summary>The document <paramref name="json"/> holds.</summary>
        /// <exception cref="ArgumentException">It is not a JSON object, or is larger or nested deeper than a document may be.</exception>
        public static PluginDocument Parse(string json)
        {
            ArgumentNullException.ThrowIfNull(json);

            ArrayBufferWriter<byte> written = new ArrayBufferWriter<byte>();
            try
            {
                using JsonDocument document = JsonDocument.Parse(json, ReadOptions);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new ArgumentException("A plugin's document is a JSON object.", nameof(json));
                }

                using Utf8JsonWriter writer = new Utf8JsonWriter(written, WriteOptions);
                document.RootElement.WriteTo(writer);
            }
            catch (JsonException exception)
            {
                throw new ArgumentException($"A plugin's document is a JSON object, nested at most {MaxDepth} deep: {exception.Message}", nameof(json), exception);
            }
            catch (InvalidOperationException exception)
            {
                // A string with half a surrogate pair, escaped, which is no Unicode text.
                throw new ArgumentException($"A plugin's document holds only valid Unicode text: {exception.Message}", nameof(json), exception);
            }

            string text = Unescaped(Encoding.UTF8.GetString(written.WrittenSpan));
            int bytes = Encoding.UTF8.GetByteCount(text);
            if (bytes > MaxBytes)
            {
                throw new ArgumentException($"A plugin's document is at most {MaxBytes} bytes; this one has {bytes}.", nameof(json));
            }

            return new PluginDocument(text);
        }

        /// <summary>The object, to read its values.</summary>
        public JsonElement ToElement()
        {
            using JsonDocument document = JsonDocument.Parse(Json, ReadOptions);
            return document.RootElement.Clone();
        }

        /// <summary>Whether <paramref name="other"/> is the same JSON, its properties in the same order.</summary>
        public bool Equals(PluginDocument? other)
        {
            return other != null && Json == other.Json;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return Equals(obj as PluginDocument);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Json.GetHashCode(StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Json;
        }

        /// <summary>
        /// <paramref name="json"/> with every escaped character JSON does not require escaped as itself: the
        /// writer escapes those beyond the basic plane (emoji) and some within it whatever its encoder, which
        /// would count an emoji as 12 bytes rather than its 4. A backslash is only ever in a string, and the
        /// writer pairs every half of a surrogate pair.
        /// </summary>
        private static string Unescaped(string json)
        {
            if (!json.Contains("\\u", StringComparison.Ordinal))
            {
                return json;
            }

            StringBuilder text = new StringBuilder(json.Length);
            for (int index = 0; index < json.Length; index++)
            {
                if (json[index] != '\\')
                {
                    text.Append(json[index]);
                    continue;
                }

                int length = json[index + 1] == 'u' ? 6 : 2;
                char escaped = length == 6 ? (char)ushort.Parse(json.AsSpan(index + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture) : '\0';
                if (escaped >= ' ' && escaped is not ('"' or '\\'))
                {
                    text.Append(escaped);
                }
                else
                {
                    text.Append(json, index, length);
                }

                index += length - 1;
            }

            return text.ToString();
        }
    }
}
