using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using ByteShelf.Plugins;
using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Services;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>A plugin's reader that does what a test says, and counts how often it was asked.</summary>
    public sealed class TestDocumentReader : IContentDocumentReader
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

        /// <summary>This as an installed plugin's reader.</summary>
        public MetadataPluginReader AsPluginReader()
        {
            return new MetadataPluginReader(this, _version, MediaTypeSet.Of(_mediaTypes));
        }

        /// <summary>Waits until every read has ended and its file is closed; throws when one is not.</summary>
        public async Task AllReadsEndedAsync()
        {
            using CancellationTokenSource limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (_streams.Any(stream => stream.CanRead))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5), limit.Token);
            }
        }

        public Task<JsonObject?> ReadAsync(Stream contents, CancellationToken cancellationToken)
        {
            _streams.Enqueue(contents);
            return _read(contents, cancellationToken);
        }
    }
}
