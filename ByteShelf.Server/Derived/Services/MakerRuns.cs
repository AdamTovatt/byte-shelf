using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Plugins.Processes;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>Makes the runs of plugins' makers.</summary>
    internal sealed class MakerRuns
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DerivedMakers _makers;
        private readonly DerivedStore _store;
        private readonly ContentsLocks _locks;

        public MakerRuns(IServiceScopeFactory scopeFactory, DerivedMakers makers, DerivedStore store, ContentsLocks locks)
        {
            _scopeFactory = scopeFactory;
            _makers = makers;
            _store = store;
            _locks = locks;
        }

        /// <summary>The run that has <paramref name="plugin"/> make what is missing of <paramref name="group"/> from the contents.</summary>
        public MakerRun Of(DerivedFilePlugin plugin, string sha256, MakingGroup group)
        {
            return new MakerRun(sha256, group, plugin, _scopeFactory, _makers, _store, _locks);
        }
    }
}
