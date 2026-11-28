using ByteShelf.Server.Metadata.Models;
using ByteShelf.Server.Metadata.Repositories;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Plugins.Services;
using ByteShelfCommon;

namespace ByteShelf.Server.Metadata.Services
{
    /// <summary>A plugin's reader, whose findings go to <c>content_plugin_metadata</c>. Made for one pass, so one that could not run is tried again by the next.</summary>
    internal sealed class PluginContentReader : ContentReader
    {
        private readonly PluginReader _reader;
        private readonly IContentPluginMetadataRepository _repository;
        private readonly PluginProblems _problems;
        private readonly ILogger _logger;
        private bool _unavailable;

        public PluginContentReader(PluginReader reader, IContentPluginMetadataRepository repository, PluginProblems problems, ILogger logger)
        {
            _reader = reader;
            _repository = repository;
            _problems = problems;
            _logger = logger;
        }

        public override bool MayRun => !_unavailable;

        public override Task<IReadOnlyList<string>> GetUnreadAsync(string? after, int limit)
        {
            return _repository.GetUnreadAsync(_reader.PluginId, _reader.Version, _reader.MediaTypes, after, limit);
        }

        public override async Task<ReadOutcome?> ReadAsync(string sha256, CancellationToken cancellationToken)
        {
            PluginReading reading;
            try
            {
                reading = await _reader.Read(sha256, cancellationToken);
            }
            catch (PluginUnavailableException exception)
            {
                _unavailable = true;
                _problems.Report(_reader.PluginId, PluginPart.Reader, PluginLane.Quick, $"Its reader of files' contents could not run, so it reads nothing until it can: {exception.Message}.");
                _logger.LogError(exception, "The reader of {Plugin} could not run, and is tried again by the next pass: {Reason}.", _reader.PluginId, exception.Message);
                return null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Stopped for the contents alone, as what was read from them is to leave nothing behind.
                return null;
            }

            if (reading is PluginReading.NotRead)
            {
                // No process ran, so it says nothing of whether one can.
                return null;
            }

            _problems.Clear(_reader.PluginId, PluginPart.Reader, PluginLane.Quick);
            return reading switch
            {
                PluginReading.Found { Json: null } => new ReadOutcome(Record(null), Failed: false),
                PluginReading.Found { Json: string json } when DocumentOf(json) is { } document => new ReadOutcome(Record(document), Failed: false),
                _ => new ReadOutcome(Record(null), Failed: true),
            };
        }

        private static PluginDocument? DocumentOf(string json)
        {
            try
            {
                return PluginDocument.Parse(json);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private RecordReading Record(PluginDocument? document)
        {
            return (sha256, at, session) => _repository.RecordAsync(ContentPluginMetadata.Of(_reader.PluginId, sha256, _reader.Version, at, document), session);
        }
    }
}
