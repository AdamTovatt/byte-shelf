using System.Text.RegularExpressions;

namespace ByteShelfCommon
{
    /// <summary>An installed plugin as it declares itself, as far as files' metadata goes.</summary>
    public sealed partial record PluginDeclaration
    {
        /// <exception cref="ArgumentException">The id is not one (<see cref="IsId"/>), the name is blank, or two fields have one name.</exception>
        public PluginDeclaration(string id, string name, IReadOnlyList<PluginField>? fields = null)
        {
            if (!IsId(id))
            {
                throw new ArgumentException($"'{id}' is not a plugin's id: lowercase letters, digits and hyphens.", nameof(id));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            Id = id;
            Name = name;
            Fields = fields ?? [];

            string? twice = Fields.GroupBy(field => field.Name, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1)?.Key;
            if (twice != null)
            {
                throw new ArgumentException($"The plugin '{id}' declares the field '{twice}' twice.", nameof(fields));
            }
        }

        /// <summary>The plugin's id (<see cref="IsId"/>), which its documents carry.</summary>
        public string Id { get; }

        /// <summary>What people call it.</summary>
        public string Name { get; }

        /// <summary>The fields it declares, each with a name of its own whichever document it is in.</summary>
        public IReadOnlyList<PluginField> Fields { get; }

        /// <summary>Whether <paramref name="id"/> may be a plugin's id: lowercase letters, digits and hyphens, one at least.</summary>
        public static bool IsId(string? id)
        {
            return id != null && IdPattern().IsMatch(id);
        }

        [GeneratedRegex(@"\A[a-z0-9-]+\z", RegexOptions.CultureInvariant)]
        private static partial Regex IdPattern();
    }
}
