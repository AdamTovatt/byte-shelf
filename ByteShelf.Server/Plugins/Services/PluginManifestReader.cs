using System.Text.Json;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Shared.IO;
using ByteShelfCommon;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>Reads what a folder's <c>plugin.json</c> says, loading nothing.</summary>
    public static class PluginManifestReader
    {
        public const string FileName = "plugin.json";

        /// <summary>Reads the folder at <paramref name="path"/>, which need not exist.</summary>
        public static PluginCandidate Read(string path)
        {
            string folder = Path.GetFullPath(path);
            try
            {
                folder = FilePaths.ResolveFolder(folder);
                using FileStream file = File.OpenRead(Path.Combine(folder, FileName));
                using JsonDocument document = JsonDocument.Parse(file);
                return Read(folder, document.RootElement);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                return Unreadable(folder, Directory.Exists(folder) ? $"It has no {FileName}." : "There is no such folder.");
            }
            catch (Exception exception)
            {
                // Whatever else a file can do wrong, such as text that is no text (a lone \ud800), is the plugin's.
                return Unreadable(folder, $"Its {FileName} could not be read: {exception.Message}");
            }
        }

        private static PluginCandidate Read(string folder, JsonElement json)
        {
            if (json.ValueKind != JsonValueKind.Object)
            {
                return Unreadable(folder, $"Its {FileName} is not a JSON object.");
            }

            string? id = Text(json, "id") is { } given && PluginDeclaration.IsId(given) ? given : null;
            string? name = Text(json, "name");
            string? version = Text(json, "version");
            UnreadablePlugin Refused(string reason) => new UnreadablePlugin(new SkippedPlugin { Folder = folder, Id = id, Name = name, Version = version, Reason = reason });

            // The id first, which says which plugin the rest is about, then the api: in a plugin for
            // another one, nothing else need mean what it means here.
            if (id == null)
            {
                return Refused("Its id must be lowercase letters, digits and hyphens.");
            }

            if (!json.TryGetProperty("api", out JsonElement api) || api.ValueKind != JsonValueKind.Number || !api.TryGetInt32(out int number))
            {
                return Refused("Its api must be a whole number.");
            }

            if (number != PluginApi.Version)
            {
                return Refused($"It is for plugin api {number}, and this server's is {PluginApi.Version}.");
            }

            if (name == null)
            {
                return Refused("It has no name.");
            }

            if (version == null)
            {
                return Refused("It has no version.");
            }

            PluginDeclaration declaration;
            try
            {
                declaration = new PluginDeclaration(id, name, Fields(json));
            }
            catch (Exception exception) when (exception is ArgumentException or PluginRefusedException)
            {
                return Refused(exception.Message);
            }

            if (!json.TryGetProperty("serverAssembly", out _))
            {
                return new ReadablePlugin(folder, new PluginManifest(declaration, version, null));
            }

            if (Text(json, "serverAssembly") is not { } relative)
            {
                return Refused("Its serverAssembly must be the path of an assembly in the plugin's folder.");
            }

            string assembly = Path.GetFullPath(relative, folder);
            if (!FilePaths.IsInside(assembly, folder))
            {
                return Refused($"Its server assembly {relative} is outside the plugin's folder.");
            }

            return File.Exists(assembly)
                ? new ReadablePlugin(folder, new PluginManifest(declaration, version, assembly))
                : Refused($"Its server assembly {relative} is not there.");
        }

        /// <summary>The fields the plugin declares; none when the file does not say.</summary>
        /// <exception cref="ArgumentException">One is not allowed.</exception>
        /// <exception cref="PluginRefusedException">They are not written as fields are.</exception>
        private static IReadOnlyList<PluginField> Fields(JsonElement json)
        {
            if (!json.TryGetProperty("fields", out JsonElement fields))
            {
                return [];
            }

            return fields.ValueKind == JsonValueKind.Array && fields.EnumerateArray().All(field => field.ValueKind == JsonValueKind.Object)
                ? fields.EnumerateArray().Select(Field).ToList()
                : throw new PluginRefusedException("Its fields must be a list of objects.");
        }

        private static PluginField Field(JsonElement json)
        {
            string name = Text(json, "name") ?? throw new PluginRefusedException("One of its fields has no name.");
            return new PluginField(
                name,
                Text(json, "label") ?? string.Empty,
                Choice<PluginFieldKind>(json, name, "kind"),
                Choice<PluginDocumentSubject>(json, name, "document"),
                Flag(json, name, "searched"),
                Flag(json, name, "listed"));
        }

        /// <summary>The value the text under <paramref name="property"/> of the field <paramref name="field"/> names, written as the API writes it.</summary>
        private static T Choice<T>(JsonElement json, string field, string property)
            where T : struct, Enum
        {
            Dictionary<string, T> choices = Enum.GetValues<T>().ToDictionary(value => JsonSerializer.SerializeToElement(value).GetString()!, StringComparer.Ordinal);
            return Text(json, property) is { } text && choices.TryGetValue(text, out T choice)
                ? choice
                : throw new PluginRefusedException($"The {property} of its field '{field}' must be one of: {string.Join(", ", choices.Keys)}.");
        }

        /// <summary>What <paramref name="property"/> of the field <paramref name="field"/> says; false when it is left out.</summary>
        private static bool Flag(JsonElement json, string field, string property)
        {
            if (!json.TryGetProperty(property, out JsonElement value))
            {
                return false;
            }

            return value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : throw new PluginRefusedException($"The {property} of its field '{field}' must be true or false.");
        }

        private static UnreadablePlugin Unreadable(string folder, string reason)
        {
            return new UnreadablePlugin(new SkippedPlugin { Folder = folder, Id = null, Name = null, Version = null, Reason = reason });
        }

        /// <summary>The text under <paramref name="property"/>; null when there is none, or it is blank or not text.</summary>
        private static string? Text(JsonElement json, string property)
        {
            return json.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()
                : null;
        }
    }
}
