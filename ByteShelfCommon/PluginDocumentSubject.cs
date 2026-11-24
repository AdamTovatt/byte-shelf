using System.Text.Json.Serialization;
using ByteShelfCommon.Serialization;

namespace ByteShelfCommon
{
    /// <summary>Which of a plugin's two documents: what it is about.</summary>
    [JsonConverter(typeof(CamelCaseEnumConverter<PluginDocumentSubject>))]
    public enum PluginDocumentSubject
    {
        /// <summary>The file's contents, as the plugin's reader read them; gone once the file is encrypted.</summary>
        Contents,

        /// <summary>The file itself, whatever its contents, as someone wrote it through the plugin.</summary>
        File,
    }
}
