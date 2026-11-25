using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Services;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>
    /// A plugin's reader that does what a test says, and counts how often it was asked, in the test's own process. What
    /// comes of it is what comes of one in a process of its own; that nothing ends it, as a swap of what it reads
    /// does there, is not.
    /// </summary>
    public sealed class TestDocumentReader
    {
        private readonly Func<Stream, CancellationToken, Task<JsonObject?>> _read;
        private readonly int _version;
        private readonly string[] _mediaTypes;
        private readonly ConcurrentQueue<Stream> _streams = new ConcurrentQueue<Stream>();

        public TestDocumentReader(Func<Stream, CancellationToken, Task<JsonObject?>> read, int version = 1, params string[] mediaTypes)
        {
            _read = read;
            _version = version;
            _mediaTypes = mediaTypes.Length == 0 ? ["text/plain"] : mediaTypes;
        }

        public int Reads => _streams.Count;

        /// <summary>How often it was told to read, whether it could run or not.</summary>
        public int Asked { get; private set; }

        /// <summary>Why it cannot run, when it cannot.</summary>
        public string? Unavailable { get; set; }

        /// <summary>A reader of text files that answers the text itself, as <c>{"text": …}</c>.</summary>
        public static TestDocumentReader OfText(int version = 1)
        {
            return new TestDocumentReader(
                async (contents, cancellationToken) => new JsonObject { ["text"] = await ReadTextAsync(contents, cancellationToken) }, version);
        }

        public static async Task<string> ReadTextAsync(Stream contents, CancellationToken cancellationToken)
        {
            using StreamReader reader = new StreamReader(contents, Encoding.UTF8, leaveOpen: true);
            return await reader.ReadToEndAsync(cancellationToken);
        }

        /// <summary>This as the reader of the plugin <paramref name="pluginId"/>, run in the test's own process.</summary>
        public PluginReader AsReaderOf(string pluginId, ObjectStore objectStore)
        {
            return new PluginReader(pluginId, _version, MediaTypeSet.Of(_mediaTypes), (sha256, cancellationToken) => RunAsync(new PluginTarget(pluginId, string.Empty), objectStore, sha256, cancellationToken));
        }

        private async Task<PluginReading> RunAsync(PluginTarget plugin, ObjectStore objectStore, string sha256, CancellationToken cancellationToken)
        {
            Asked++;
            if (Unavailable != null)
            {
                throw new PluginUnavailableException(Unavailable);
            }

            PluginRunResult result;
            try
            {
                await using FileStream contents = objectStore.OpenRead(sha256);
                result = new PluginRunResult.Answered(JsonSerializer.SerializeToElement(await ReadAsync(contents, cancellationToken)));
            }
            catch (FileNotFoundException)
            {
                result = new PluginRunResult.ContentsNotRead();
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                result = new PluginRunResult.Failed(exception.GetType().FullName!);
            }

            return await new ReadRun(plugin, sha256, _version).TakeAsync(result, null!, cancellationToken);
        }

        private Task<JsonObject?> ReadAsync(Stream contents, CancellationToken cancellationToken)
        {
            _streams.Enqueue(contents);
            return _read(contents, cancellationToken);
        }
    }
}
