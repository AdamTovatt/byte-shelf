using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Storage.Repositories;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>Makes what is made in the background (<see cref="MakingGroup.Background"/>): of contents just stored, and of those stored already.</summary>
    internal sealed class DerivedBackground
    {
        /// <summary>How many contents one query lists.</summary>
        internal const int BatchSize = 500;

        private readonly IPlainContentsRepository _contents;
        private readonly DerivedMakers _makers;
        private readonly DerivedMaking _making;

        public DerivedBackground(IPlainContentsRepository contents, DerivedMakers makers, DerivedMaking making)
        {
            _contents = contents;
            _makers = makers;
            _making = making;
        }

        /// <summary>
        /// Has <paramref name="plugin"/> make what is missing of every contents a live plain file holds, whenever it
        /// was stored; answers how many contents it ran for.
        /// </summary>
        /// <remarks>
        /// Looks on disk for every contents' files each time, with no index of what is missing: a cache somebody
        /// emptied is made again, which an index beside it would not notice.
        /// </remarks>
        /// <exception cref="PluginUnavailableException">The plugin could not run.</exception>
        public async Task<int> MakeBacklogAsync(DerivedFilePlugin plugin, CancellationToken cancellationToken)
        {
            MediaTypeSet mediaTypes = MediaTypeSet.Union(
                plugin.Makes.Where(declaration => DerivedKinds.GroupOf(declaration.Kind) == MakingGroup.Background).Select(declaration => declaration.MediaTypes));

            int made = 0;
            string? after = null;
            while (await _contents.GetLiveAsync(mediaTypes, after, BatchSize) is { Count: > 0 } batch)
            {
                foreach ((string sha256, IReadOnlyList<string> held) in batch)
                {
                    after = sha256;
                    made += await MakeMissingAsync(plugin, sha256, held, PluginPriority.Backlog, cancellationToken);
                }
            }

            return made;
        }

        /// <summary>Has <paramref name="plugin"/> make what is missing of contents just stored.</summary>
        /// <exception cref="PluginUnavailableException">The plugin could not run.</exception>
        public async Task MakeStoredAsync(DerivedFilePlugin plugin, string sha256, CancellationToken cancellationToken)
        {
            await MakeMissingAsync(plugin, sha256, await _contents.GetMediaTypesAsync(sha256), PluginPriority.Stored, cancellationToken);
        }

        private async Task<int> MakeMissingAsync(DerivedFilePlugin plugin, string sha256, IReadOnlyList<string> mediaTypes, PluginPriority priority, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<KindMaker> missing = _makers.MissingOf(plugin, MakingGroup.Background, sha256, mediaTypes);
            if (missing.Count == 0)
            {
                return 0;
            }

            MakingOutcome outcome = await _making.MakeAsync(sha256, missing[0].Declaration.Kind, plugin, priority).WaitAsync(cancellationToken);
            return outcome == MakingOutcome.Kept ? 1 : 0;
        }
    }
}
