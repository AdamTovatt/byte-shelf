using System.Text.Json.Nodes;
using ByteShelf.Plugins;

namespace ByteShelf.Server.Tests.Plugin
{
    /// <summary>Answers what a text says, as <c>{"words": …}</c>. It cannot be made while the plugin's folder holds <see cref="FailingFileName"/>.</summary>
    public sealed class WordsReader : IContentDocumentReader
    {
        private const string FailingFileName = "fail-reader";

        // Made with a service of the plugin's own, as a reader may be.
        public WordsReader(PluginGreeting greeting)
        {
            ArgumentNullException.ThrowIfNull(greeting);
            if (File.Exists(Path.Combine(Path.GetDirectoryName(typeof(WordsReader).Assembly.Location)!, FailingFileName)))
            {
                throw new InvalidOperationException("It was told to fail.");
            }
        }

        public async Task<JsonObject?> ReadAsync(Stream contents, CancellationToken cancellationToken)
        {
            using StreamReader reader = new StreamReader(contents, leaveOpen: true);
            return new JsonObject { ["words"] = await reader.ReadToEndAsync(cancellationToken) };
        }
    }
}
