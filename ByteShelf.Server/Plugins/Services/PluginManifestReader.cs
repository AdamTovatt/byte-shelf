using System.Text.Json;
using System.Text.RegularExpressions;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Shared.IO;

namespace ByteShelf.Server.Plugins.Services
{
    /// <summary>Reads what a folder's <c>plugin.json</c> says, loading nothing.</summary>
    public static partial class PluginManifestReader
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

            string? id = Text(json, "id") is { } given && IdPattern().IsMatch(given) ? given : null;
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

            if (!json.TryGetProperty("serverAssembly", out _))
            {
                return new ReadablePlugin(folder, new PluginManifest(id, name, version, null));
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
                ? new ReadablePlugin(folder, new PluginManifest(id, name, version, assembly))
                : Refused($"Its server assembly {relative} is not there.");
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

        [GeneratedRegex(@"^[a-z0-9-]+\z", RegexOptions.CultureInvariant)]
        private static partial Regex IdPattern();
    }
}
