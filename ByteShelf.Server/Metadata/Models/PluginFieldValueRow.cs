using System.Text.Json;
using ByteShelfCommon;
using ByteShelfCommon.Serialization;

namespace ByteShelf.Server.Metadata.Models
{
    /// <summary>A <c>plugin_field_value</c> row, but for what its document is about: the value of a declared field.</summary>
    /// <param name="Field">The field's name.</param>
    /// <param name="TextFold">A text, as search compares it.</param>
    /// <param name="Number">A number.</param>
    /// <param name="Instant">An instant.</param>
    /// <param name="Flag">A flag.</param>
    /// <param name="Searched">Whether a search's free text matches it.</param>
    /// <param name="Listed">The value as listings carry it; null when they do not.</param>
    public sealed record PluginFieldValueRow(string Field, string? TextFold, double? Number, DateTimeOffset? Instant, bool? Flag, bool Searched, string? Listed)
    {
        /// <summary>
        /// What is taken from <paramref name="document"/>, about <paramref name="subject"/>: the values it has of the
        /// fields <paramref name="plugin"/> declares there. A change of what this answers raises
        /// <see cref="Services.PluginFieldIndex.Version"/>.
        /// </summary>
        public static IReadOnlyList<PluginFieldValueRow> Of(PluginDeclaration plugin, PluginDocumentSubject subject, PluginDocument document)
        {
            JsonElement read = document.ToElement();
            List<PluginFieldValueRow> rows = new List<PluginFieldValueRow>();
            foreach (PluginField field in plugin.Fields.Where(field => field.Document == subject))
            {
                if (PluginFieldValue.Of(field, read) is { } value)
                {
                    string? listed = field.Listed && value.FitsListings() ? JsonSerializer.Serialize(value, ByteShelfJsonContext.Default.PluginFieldValue) : null;
                    rows.Add(new PluginFieldValueRow(field.Name, value.FoldedText(), value.Number, value.Date, value.Flag, field.Searched, listed));
                }
            }

            return rows;
        }
    }
}
