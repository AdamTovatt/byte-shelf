using System.Net.Http.Headers;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Repositories;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>
    /// Runs a plugin's maker over one contents in a child process and keeps what it makes. A run starts, and what it
    /// made is kept, only while a plain file holds the contents.
    /// </summary>
    internal sealed class MakerRuns
    {
        /// <summary>How long a run may take.</summary>
        private static readonly TimeSpan RunLimit = TimeSpan.FromSeconds(60);

        private readonly Lock _lock = new Lock();
        private readonly List<LeftProcess> _left = new List<LeftProcess>();

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DerivedMakers _makers;
        private readonly DerivedStore _store;
        private readonly ObjectStore _objectStore;
        private readonly MakerExecutable _executable;
        private readonly RootFolder _root;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<MakerRuns> _logger;

        public MakerRuns(
            IServiceScopeFactory scopeFactory,
            DerivedMakers makers,
            DerivedStore store,
            ObjectStore objectStore,
            MakerExecutable executable,
            RootFolder root,
            TimeProvider timeProvider,
            ILogger<MakerRuns> logger)
        {
            _scopeFactory = scopeFactory;
            _makers = makers;
            _store = store;
            _objectStore = objectStore;
            _executable = executable;
            _root = root;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        /// <summary>
        /// Has <paramref name="plugin"/> make what is missing of <paramref name="group"/> from the contents. When this
        /// ends, however it does, the process is gone and its folder is, or <see cref="RemoveLeftAsync"/> knows of them.
        /// </summary>
        /// <exception cref="OperationCanceledException">It was stopped, which says nothing of the contents.</exception>
        /// <exception cref="MakerUnavailableException">The plugin could not run, whatever the contents.</exception>
        public async Task<MakingOutcome> RunAsync(string sha256, MakingGroup group, DerivedFilePlugin plugin, CancellationToken cancellationToken)
        {
            IReadOnlyList<KindMaker> missing;
            using (await _store.LockAsync(sha256, cancellationToken))
            {
                missing = _makers.MissingOf(plugin, group, sha256, await GetMediaTypesAsync(sha256));
            }

            if (missing.Count == 0)
            {
                return MakingOutcome.NothingToMake;
            }

            MakingFolder folder = MakingFolder.Create(_root, sha256);
            try
            {
                folder.WriteRequest(new MakingRequest(
                    plugin.Location,
                    _objectStore.GetPath(sha256),
                    missing.Select(maker => new RequestedKind(maker.Declaration.Kind, maker.Declaration.Version)).ToList()));

                MakerRun run = await RunProcessAsync(sha256, folder, cancellationToken);
                switch (run)
                {
                    case MakerRun.ContentsNotOpened:
                        _logger.LogWarning("The plugin {Plugin} made nothing of the contents {Sha256}, which could not be opened.", plugin.Id, sha256);
                        return MakingOutcome.NothingToMake;
                    case MakerRun.Failed failed:
                        _logger.LogInformation("The plugin {Plugin} could not make derived files from the contents {Sha256}: {Reason}.", plugin.Id, sha256, failed.Reason);
                        break;
                }

                MakingOutcome outcome;
                using (await _store.LockAsync(sha256, cancellationToken))
                {
                    outcome = await KeepAsync(sha256, missing, folder, (run as MakerRun.Made)?.Files ?? []);
                }

                if (group == MakingGroup.Full)
                {
                    _store.TrimFull();
                }

                return outcome;
            }
            finally
            {
                Remove(folder);
            }
        }

        /// <summary>
        /// Ends every process and removes every folder a run over the contents left, this server's or an earlier
        /// one's. Hold the contents' lock, with nothing being made from them.
        /// </summary>
        /// <exception cref="MakerNotStoppedException">One is still there.</exception>
        public async Task RemoveLeftAsync(string sha256)
        {
            List<LeftProcess> left;
            lock (_lock)
            {
                left = _left.Where(process => process.Sha256 == sha256).ToList();
            }

            foreach (LeftProcess process in left)
            {
                await process.Process.EndAsync();
                process.Process.Dispose();
                lock (_lock)
                {
                    _left.Remove(process);
                }
            }

            foreach (MakingFolder folder in MakingFolder.Find(_root, sha256))
            {
                try
                {
                    folder.Remove();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new MakerNotStoppedException($"What was being made in {folder.Path} could not be removed.", exception);
                }
            }
        }

        private static bool IsMediaType(string value)
        {
            return MediaTypeHeaderValue.TryParse(value, out _);
        }

        private static MakerRun OutcomeOf(MakingFolder folder, int exitCode)
        {
            switch ((MakingExitCode)exitCode)
            {
                case MakingExitCode.Made:
                    return folder.ReadMade() is { } made ? new MakerRun.Made(made) : new MakerRun.Failed("its answer could not be read");
                case MakingExitCode.Failed when folder.ReadFailure() is string failure:
                    return new MakerRun.Failed(failure);
                case MakingExitCode.ContentsNotOpened:
                    return new MakerRun.ContentsNotOpened();
                case MakingExitCode.PluginUnavailable:
                    throw new MakerUnavailableException("it could not be loaded, or is another version than when the server started");
                case MakingExitCode.NotKept:
                    throw new MakerUnavailableException("what it made could not be written");
                default:
                    return folder.IsStarted()
                        ? new MakerRun.Failed($"its process ended with exit code {exitCode}")
                        : throw new MakerUnavailableException($"its process ended with exit code {exitCode} before it ran");
            }
        }

        private async Task<MakerRun> RunProcessAsync(string sha256, MakingFolder folder, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MakerProcess process = MakerProcess.Start(_executable, folder, _timeProvider);
            int? exitCode = null;
            try
            {
                exitCode = await process.ExitsWithinAsync(RunLimit, cancellationToken);
            }
            finally
            {
                await EndAsync(sha256, process);
            }

            return exitCode is int ended ? OutcomeOf(folder, ended) : new MakerRun.Failed($"it took over {RunLimit}");
        }

        private async Task EndAsync(string sha256, MakerProcess process)
        {
            try
            {
                await process.EndAsync();
                process.Dispose();
            }
            catch (MakerNotStoppedException exception)
            {
                lock (_lock)
                {
                    _left.Add(new LeftProcess(sha256, process));
                }

                _logger.LogError(exception, "A maker of the contents {Sha256} goes on though it was killed.", sha256);
            }
        }

        /// <summary>Keeps what was made, and that the rest of what was asked for could not be, as long as a plain file holds the contents. Hold the lock.</summary>
        private async Task<MakingOutcome> KeepAsync(string sha256, IReadOnlyList<KindMaker> asked, MakingFolder folder, IReadOnlyList<MadeInFolder> made)
        {
            if ((await GetMediaTypesAsync(sha256)).Count == 0)
            {
                return MakingOutcome.NothingToMake;
            }

            foreach (KindMaker maker in asked)
            {
                DerivedKind kind = maker.Declaration.Kind;
                FileInfo file = new FileInfo(folder.PathOf(kind));
                MadeInFolder? found = made.FirstOrDefault(candidate => candidate.Kind == kind && IsMediaType(candidate.MediaType));
                bool isMade = found != null && file.Exists && file.Length <= DerivedKinds.MaxBytesOf(kind);
                _store.Put(
                    kind,
                    sha256,
                    new DerivedEntry(maker.Plugin.Id, maker.Declaration.Version, isMade ? new MadeInfo(found!.MediaType, found.Original) : null),
                    isMade ? file.FullName : null);
            }

            return MakingOutcome.Kept;
        }

        private async Task<IReadOnlyList<string>> GetMediaTypesAsync(string sha256)
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ISourceContentsRepository>().GetMediaTypesAsync(sha256);
        }

        private void Remove(MakingFolder folder)
        {
            try
            {
                folder.Remove();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(exception, "Could not remove {Folder}, which holds what was read from contents; remove it.", folder.Path);
            }
        }

        private sealed record LeftProcess(string Sha256, MakerProcess Process);
    }
}
