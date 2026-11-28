using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Plugins.Services;
using ByteShelf.Server.Shared.Root;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>Makes the runs of plugins' makers.</summary>
    internal sealed class MakerRuns
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DerivedMakers _makers;
        private readonly PluginProblems _problems;
        private readonly RootFolder _root;

        public MakerRuns(IServiceScopeFactory scopeFactory, DerivedMakers makers, PluginProblems problems, RootFolder root)
        {
            _scopeFactory = scopeFactory;
            _makers = makers;
            _problems = problems;
            _root = root;
        }

        /// <summary>The run that has <paramref name="plugin"/> make what is missing of <paramref name="group"/> from the contents.</summary>
        public MakerRun Of(DerivedFilePlugin plugin, string sha256, MakingGroup group)
        {
            return new MakerRun(sha256, group, plugin, _scopeFactory, _makers, _problems, _root);
        }
    }
}
