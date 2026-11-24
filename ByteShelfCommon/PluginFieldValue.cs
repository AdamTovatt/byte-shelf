using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ByteShelfCommon
{
    /// <summary>The value of a plugin's declared field: exactly one of its parts is there, the one its kind names.</summary>
    public sealed partial record PluginFieldValue
    {
        /// <summary>The most bytes of UTF-8 a text may have to be carried by listings; a longer one is left out of them.</summary>
        public const int MaxListedBytes = 256;

        /// <summary>How much of a text is searched, in code points from its start.</summary>
        public const int MaxSearchedCodePoints = 1000;

        /// <summary>Reads one from JSON; make one with <see cref="OfText"/>, <see cref="OfNumber"/>, <see cref="OfDate"/> or <see cref="OfFlag"/>.</summary>
        /// <exception cref="ArgumentException">Not exactly one part is there.</exception>
        [JsonConstructor]
        internal PluginFieldValue(string? text, double? number, DateTimeOffset? date, bool? flag)
        {
            if ((text != null ? 1 : 0) + (number != null ? 1 : 0) + (date != null ? 1 : 0) + (flag != null ? 1 : 0) != 1)
            {
                throw new ArgumentException("A plugin field's value is one of a text, a number, a date and a flag.");
            }

            Text = text;
            Number = number;
            Date = date;
            Flag = flag;
        }

        /// <summary>A text.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Text { get; }

        /// <summary>A number.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Number { get; }

        /// <summary>An instant.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTimeOffset? Date { get; }

        /// <summary>A flag.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Flag { get; }

        /// <summary>The text <paramref name="text"/>.</summary>
        public static PluginFieldValue OfText(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            return new PluginFieldValue(text, number: null, date: null, flag: null);
        }

        /// <summary>The number <paramref name="number"/>.</summary>
        public static PluginFieldValue OfNumber(double number)
        {
            return new PluginFieldValue(text: null, number, date: null, flag: null);
        }

        /// <summary>The instant <paramref name="date"/>.</summary>
        public static PluginFieldValue OfDate(DateTimeOffset date)
        {
            return new PluginFieldValue(text: null, number: null, date, flag: null);
        }

        /// <summary>The flag <paramref name="flag"/>.</summary>
        public static PluginFieldValue OfFlag(bool flag)
        {
            return new PluginFieldValue(text: null, number: null, date: null, flag);
        }

        /// <summary>
        /// The value of <paramref name="field"/> in <paramref name="document"/>, the one the field is in
        /// (<see cref="PluginDocument.ToElement"/>, read once for all its fields; null for no document); null
        /// when it has none, or one of another JSON type.
        /// </summary>
        public static PluginFieldValue? Of(PluginField field, JsonElement? document)
        {
            ArgumentNullException.ThrowIfNull(field);
            return document is { } read && read.TryGetProperty(field.Name, out JsonElement element)
                ? Of(field.Kind, element)
                : null;
        }

        /// <summary>Whether listings carry it: a text of at most <see cref="MaxListedBytes"/>, or anything else.</summary>
        public bool FitsListings()
        {
            return Text == null || Encoding.UTF8.GetByteCount(Text) <= MaxListedBytes;
        }

        /// <summary>The text as search compares it, its first <see cref="MaxSearchedCodePoints"/> folded by <see cref="FileCriteria.Fold"/>; null for any other value.</summary>
        public string? FoldedText()
        {
            return Text == null ? null : FileCriteria.Fold(Cut(Text));
        }

        private static string Cut(string text)
        {
            int length = 0;
            int codePoints = 0;
            foreach (Rune rune in text.EnumerateRunes())
            {
                if (codePoints++ == MaxSearchedCodePoints)
                {
                    return text[..length];
                }

                length += rune.Utf16SequenceLength;
            }

            return text;
        }

        private static PluginFieldValue? Of(PluginFieldKind kind, JsonElement element)
        {
            switch (kind)
            {
                case PluginFieldKind.Text when element.ValueKind == JsonValueKind.String:
                    return OfText(element.GetString()!);

                // A number beyond what a double holds reads as infinite, which is no value to compare.
                case PluginFieldKind.Number when element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double number) && double.IsFinite(number):
                    return OfNumber(number);

                case PluginFieldKind.Date when element.ValueKind == JsonValueKind.String && TryReadInstant(element.GetString()!, out DateTimeOffset date):
                    return OfDate(date);

                case PluginFieldKind.Flag when element.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    return OfFlag(element.GetBoolean());

                default:
                    return null;
            }
        }

        /// <summary>Reads an ISO 8601 date and time with an offset, the one way a document writes an instant, as UTC.</summary>
        internal static bool TryReadInstant(string text, out DateTimeOffset instant)
        {
            bool read = InstantPattern().IsMatch(text)
                & DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out instant);
            return read;
        }

        [GeneratedRegex(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}(:[0-9]{2}(\.[0-9]{1,7})?)?(Z|[+-][0-9]{2}:[0-9]{2})\z", RegexOptions.CultureInvariant)]
        private static partial Regex InstantPattern();
    }
}
