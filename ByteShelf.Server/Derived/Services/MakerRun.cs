using System.Net.Http.Headers;
using System.Text.Json;
using ByteShelf.Plugins;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Shared.Json;
using ByteShelf.Server.Storage.Repositories;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>
    /// Has a plugin's maker make what is missing of one group from one contents, and keeps it, if a plain file still
    /// holds them.
    /// </summary>
    internal sealed class MakerRun : IPluginRun<MakingOutcome>
    {
        private readonly DerivedFilePlugin _plugin;
        private readonly MakingGroup _group;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DerivedMakers _makers;
        private readonly DerivedStore _store;
        private readonly ContentsLocks _locks;
        private IReadOnlyList<KindMaker> _missing = [];

        public MakerRun(
            string sha256,
            MakingGroup group,
            DerivedFilePlugin plugin,
            IServiceScopeFactory scopeFactory,
            DerivedMakers makers,
            DerivedStore store,
            ContentsLocks locks)
        {
            Sha256 = sha256;
            _group = group;
            _plugin = plugin;
            _scopeFactory = scopeFactory;
            _makers = makers;
            _store = store;
            _locks = locks;
        }

        public PluginTarget Plugin => _plugin.Target;

        public string Sha256 { get; }

        public string Work => MakeWork.Name;

        public MakingOutcome Nothing => MakingOutcome.NothingToMake;

        public JsonElement? Prepare(IReadOnlyList<string> mediaTypes)
        {
            _missing = _makers.MissingOf(_plugin, _group, Sha256, mediaTypes);
            return _missing.Count == 0
                ? null
                : JsonSerializer.SerializeToElement(
                    new MakeArguments(_missing.Select(maker => new RequestedKind(maker.Declaration.Kind, maker.Declaration.Version)).ToList()), JsonDefaults.CamelCase);
        }

        public async Task<MakingOutcome> TakeAsync(PluginRunResult result, WorkFolder folder, CancellationToken cancellationToken)
        {
            if (result is PluginRunResult.ContentsNotRead)
            {
                return MakingOutcome.NothingToMake;
            }

            IReadOnlyList<MadeInFolder> made = result is PluginRunResult.Answered answered ? MadeIn(answered.Answer) : [];
            using (await _locks.LockAsync(Sha256, cancellationToken))
            {
                if (!await AreHeldAsync())
                {
                    return MakingOutcome.NothingToMake;
                }

                _store.Put(Sha256, _missing.Select(maker => ToKeep(maker, made.FirstOrDefault(file => file.Kind == maker.Declaration.Kind), folder)).ToList());
            }

            if (_group == MakingGroup.Full)
            {
                _store.TrimFull();
            }

            return MakingOutcome.Kept;
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

        /// <summary>What is kept of one kind: what was made, or that it could not be.</summary>
        private KeptFile ToKeep(KindMaker maker, MadeInFolder? made, WorkFolder folder)
        {
            DerivedKind kind = maker.Declaration.Kind;

            // Whatever wrote it: a maker can leave a file there by its path, too.
            FileInfo file = new FileInfo(Path.Combine(folder.Path, DerivedKinds.NameOf(kind)));
            bool mayBeKept = file is { Exists: true, LinkTarget: null } && file.Length <= DerivedFileLimits.MaxBytesOf(kind);
            MadeInfo? info = made != null && MediaTypeHeaderValue.TryParse(made.MediaType, out _) && mayBeKept
                ? new MadeInfo(made.MediaType, made.Original)
                : null;

            return new KeptFile(kind, new DerivedEntry(_plugin.Id, maker.Declaration.Version, info), info == null ? null : file.FullName);
        }

        private async Task<bool> AreHeldAsync()
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<IPlainContentsRepository>().GetUnheldAsync([Sha256])).Count == 0;
        }
    }
}
