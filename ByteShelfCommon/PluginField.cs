using System.Text.RegularExpressions;

namespace ByteShelfCommon
{
    /// <summary>
    /// A field a plugin declares: a property at the top of one of its documents that the server shows, finds
    /// files by and, when the plugin says so, gives with listings. A value of another JSON type than the
    /// field's kind is ignored, as if it were not there.
    /// </summary>
    public sealed partial record PluginField
    {
        /// <summary>The most characters of a name.</summary>
        public const int MaxNameLength = 64;

        /// <exception cref="ArgumentException">The name or label is not allowed, or a field that is not text is searched.</exception>
        public PluginField(string name, string label, PluginFieldKind kind, PluginDocumentSubject document, bool searched = false, bool listed = false)
        {
            ArgumentNullException.ThrowIfNull(name);
            if (!IsName(name))
            {
                throw new ArgumentException($"'{name}' is not a field's name: a letter, then letters, digits, hyphens and underscores, {MaxNameLength} characters at most.");
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                throw new ArgumentException($"The field '{name}' has no label.");
            }

            if (searched && kind != PluginFieldKind.Text)
            {
                throw new ArgumentException($"The field '{name}' is searched by free text, which only a text field can be.");
            }

            Name = name;
            Label = label;
            Kind = kind;
            Document = document;
            Searched = searched;
            Listed = listed;
        }

        /// <summary>The property of the document, at its top.</summary>
        public string Name { get; }

        /// <summary>What people read.</summary>
        public string Label { get; }

        /// <summary>What it holds.</summary>
        public PluginFieldKind Kind { get; }

        /// <summary>The document it is in.</summary>
        public PluginDocumentSubject Document { get; }

        /// <summary>Whether a search's free text matches it, as it does a file's name; text only.</summary>
        public bool Searched { get; }

        /// <summary>Whether listings carry it (<see cref="ShelfFile.PluginFields"/>), up to <see cref="PluginFieldValue.MaxListedBytes"/>.</summary>
        public bool Listed { get; }

        internal static bool IsName(string name)
        {
            return name.Length <= MaxNameLength && NamePattern().IsMatch(name);
        }

        [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9_-]*\z", RegexOptions.CultureInvariant)]
        private static partial Regex NamePattern();
    }
}
