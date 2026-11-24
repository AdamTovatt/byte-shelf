using System.Text.Json;

namespace ByteShelfCommon
{
    /// <summary>What one installed plugin keeps about a file.</summary>
    public sealed class PluginFileMetadata
    {
        /// <summary>The plugin's name, for people.</summary>
        public required string Name { get; init; }

        /// <summary>What its reader found in the file's contents; null for nothing, for contents not read yet and for an encrypted file.</summary>
        public PluginDocument? ContentsDocument { get; init; }

        /// <summary>What it keeps about the file itself; null for nothing.</summary>
        public PluginDocument? FileDocument { get; init; }

        /// <summary>
        /// The version of <see cref="FileDocument"/>, which a write of it names; null before its first write.
        /// It stays, and goes on counting, when the document is removed.
        /// </summary>
        public int? FileVersion { get; init; }

        /// <summary>The fields the plugin declares that have a value here, in the order it declares them.</summary>
        public required IReadOnlyList<PluginFieldEntry> Fields { get; init; }

        /// <summary>What <paramref name="plugin"/> keeps about a file, its declared fields read from its documents.</summary>
        public static PluginFileMetadata Of(PluginDeclaration plugin, PluginDocument? contentsDocument, PluginDocument? fileDocument, int? fileVersion)
        {
            ArgumentNullException.ThrowIfNull(plugin);
            JsonElement? contents = contentsDocument?.ToElement();
            JsonElement? file = fileDocument?.ToElement();
            List<PluginFieldEntry> fields = new List<PluginFieldEntry>();
            foreach (PluginField field in plugin.Fields)
            {
                if (PluginFieldValue.Of(field, field.Document == PluginDocumentSubject.Contents ? contents : file) is { } value)
                {
                    fields.Add(new PluginFieldEntry { Name = field.Name, Label = field.Label, Kind = field.Kind, Document = field.Document, Value = value });
                }
            }

            return new PluginFileMetadata
            {
                Name = plugin.Name,
                ContentsDocument = contentsDocument,
                FileDocument = fileDocument,
                FileVersion = fileVersion,
                Fields = fields,
            };
        }
    }

    /// <summary>A plugin's declared field with its value on one file.</summary>
    public sealed class PluginFieldEntry
    {
        /// <summary>The field's name.</summary>
        public required string Name { get; init; }

        /// <summary>What people read.</summary>
        public required string Label { get; init; }

        /// <summary>What it holds.</summary>
        public required PluginFieldKind Kind { get; init; }

        /// <summary>The document it is in.</summary>
        public required PluginDocumentSubject Document { get; init; }

        /// <summary>Its value.</summary>
        public required PluginFieldValue Value { get; init; }
    }
}
