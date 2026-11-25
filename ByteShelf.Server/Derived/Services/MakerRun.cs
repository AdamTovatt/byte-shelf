using System.Net.Http.Headers;
using System.Text.Json;
using ByteShelf.Plugins;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Shared.Json;
using ByteShelf.Server.Shared.Root;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;
using EasyReasy.Database;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>
    /// Has a plugin's maker make what is missing of one group from one contents, takes what it made out of its process's
    /// folder, and once the run is over keeps it, if a plain file still holds them (<see cref="KeepAsync"/>).
    /// </summary>
    internal sealed class MakerRun : IPluginRun<TakenFiles?>
    {
        private readonly DerivedFilePlugin _plugin;
        private readonly MakingGroup _group;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DerivedMakers _makers;
        private readonly RootFolder _root;
        private IReadOnlyList<KindMaker> _missing = [];

        public MakerRun(
            string sha256,
            MakingGroup group,
            DerivedFilePlugin plugin,
            IServiceScopeFactory scopeFactory,
            DerivedMakers makers,
            RootFolder root)
        {
            Sha256 = sha256;
            _group = group;
            _plugin = plugin;
            _scopeFactory = scopeFactory;
            _makers = makers;
            _root = root;
        }

        public PluginTarget Plugin => _plugin.Target;

        public string Sha256 { get; }

        public Type Work => typeof(MakeWork);

        public TakenFiles? Nothing => null;

        public JsonElement? Prepare(IReadOnlyList<string> mediaTypes)
        {
            _missing = _makers.MissingOf(_plugin, _group, Sha256, mediaTypes);
            return _missing.Count == 0
                ? null
                : JsonSerializer.SerializeToElement(
                    new MakeArguments(_missing.Select(maker => new RequestedKind(maker.Declaration.Kind, maker.Declaration.Version)).ToList()), JsonDefaults.CamelCase);
        }

        /// <summary>
        /// Moves what was made into a folder of its own that notes the contents, so a removal of them finds it as it finds
        /// a process's: kept, it waits for the database, which a removal holds while it waits for the run and its process.
        /// </summary>
        public Task<TakenFiles?> TakeAsync(PluginRunResult result, WorkFolder folder, CancellationToken cancellationToken)
        {
            if (result is PluginRunResult.ContentsNotRead)
            {
                return Task.FromResult<TakenFiles?>(null);
            }

            IReadOnlyList<MadeInFolder> made = result is PluginRunResult.Answered answered ? MadeIn(answered.Answer) : [];
            WorkFolder taken = WorkFolder.Create(_root);
            try
            {
                taken.Add(Sha256);
                List<KeptFile> files = _missing.Select(maker => Take(maker, made.FirstOrDefault(file => file.Kind == maker.Declaration.Kind), folder, taken)).ToList();
                return Task.FromResult<TakenFiles?>(new TakenFiles(taken, files));
            }
            catch
            {
                RemoveQuietly(taken);
                throw;
            }
        }

        /// <summary>
        /// Keeps what was taken, unless no plain file holds the contents any more or a removal of them took it, and removes
        /// the rest with its folder. Of what was taken, the next to ask makes it again.
        /// </summary>
        public async Task<MakingOutcome> KeepAsync(TakenFiles taken, CancellationToken cancellationToken)
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            IPlainContentsRepository contents = scope.ServiceProvider.GetRequiredService<IPlainContentsRepository>();
            DerivedFiles files = scope.ServiceProvider.GetRequiredService<DerivedFiles>();
            MakingOutcome outcome;
            try
            {
                outcome = await scope.ServiceProvider.GetRequiredService<ContentsTransactions>().RunAsync(Sha256, async session =>
                {
                    if ((await contents.GetUnheldAsync([Sha256], session)).Count > 0 || taken.WasRemoved)
                    {
                        return MakingOutcome.NothingToMake;
                    }

                    await files.KeepAsync(Sha256, taken.Files, session);
                    return MakingOutcome.Kept;
                }, cancellationToken);
            }
            finally
            {
                RemoveQuietly(taken.Folder);
            }

            if (outcome == MakingOutcome.Kept && _group == MakingGroup.Full)
            {
                await files.TrimFullAsync();
            }

            return outcome;
        }

        private static IReadOnlyList<MadeInFolder> MadeIn(JsonElement answer)
        {
            try
            {
                return answer.Deserialize<List<MadeInFolder>>(JsonDefaults.CamelCase) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        private static void RemoveQuietly(WorkFolder folder)
        {
            try
            {
                folder.Remove();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Found by its note: by the removal of the contents, maintenance once no plain file holds them, or the next start.
            }
        }

        /// <summary>What is to be kept of one kind, moved to <paramref name="taken"/>: what was made, or that it could not be.</summary>
        private KeptFile Take(KindMaker maker, MadeInFolder? made, WorkFolder folder, WorkFolder taken)
        {
            DerivedKind kind = maker.Declaration.Kind;

            // Whatever wrote it: a maker can leave a file there by its path, too.
            FileInfo file = new FileInfo(Path.Combine(folder.Path, DerivedKinds.NameOf(kind)));
            bool mayBeKept = file is { Exists: true, LinkTarget: null } && file.Length <= DerivedFileLimits.MaxBytesOf(kind);
            if (made == null || !MediaTypeHeaderValue.TryParse(made.MediaType, out _) || !mayBeKept)
            {
                return KeptFile.Failed(kind, _plugin.Id, maker.Declaration.Version);
            }

            string source = Path.Combine(taken.Path, DerivedKinds.NameOf(kind));
            file.MoveTo(source);
            return KeptFile.Made(kind, _plugin.Id, maker.Declaration.Version, made.MediaType, source);
        }
    }
}
