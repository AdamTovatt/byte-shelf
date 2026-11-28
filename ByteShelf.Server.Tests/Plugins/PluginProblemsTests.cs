using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Plugins.Services;

namespace ByteShelf.Server.Tests.Plugins
{
    public sealed class PluginProblemsTests
    {
        private readonly PluginProblems _problems = new PluginProblems();

        [Fact]
        public void Of_ALane_IsItsOwnProblemAlone()
        {
            _problems.Report("p", PluginPart.Maker, PluginLane.Long, "Long.");

            Assert.Equal(("Long.", null), (_problems.Of("p", PluginPart.Maker, PluginLane.Long), _problems.Of("p", PluginPart.Maker, PluginLane.Quick)));
        }

        [Fact]
        public void Of_EveryLane_IsEachProblemQuickFirst()
        {
            _problems.Report("p", PluginPart.Maker, PluginLane.Long, "Long.");
            _problems.Report("p", PluginPart.Maker, PluginLane.Quick, "Quick.");

            Assert.Equal("Quick. Long.", _problems.Of("p", PluginPart.Maker));
        }

        [Fact]
        public void Clear_ALane_LeavesTheOthers()
        {
            _problems.Report("p", PluginPart.Maker, PluginLane.Long, "Long.");
            _problems.Report("p", PluginPart.Maker, PluginLane.Quick, "Quick.");

            _problems.Clear("p", PluginPart.Maker, PluginLane.Quick);

            Assert.Equal("Long.", _problems.Of("p", PluginPart.Maker));
        }

        [Fact]
        public void Of_NoProblem_IsNone()
        {
            _problems.Report("p", PluginPart.Reader, PluginLane.Quick, "Reader.");

            Assert.Null(_problems.Of("p", PluginPart.Maker));
            Assert.Null(_problems.Of("other", PluginPart.Reader));
        }
    }
}
