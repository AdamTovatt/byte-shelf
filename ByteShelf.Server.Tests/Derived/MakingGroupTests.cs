using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Jobs.Models;
using ByteShelf.Server.Plugins.Processes;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Derived
{
    public sealed class MakingGroupTests
    {
        [Fact]
        public void Of_EveryKind_IsTheOneGroupThatHasIt()
        {
            Assert.All(DerivedKinds.All, kind => Assert.Single(MakingGroup.All, group => group.Kinds.Contains(kind)));
            Assert.All(DerivedKinds.All, kind => Assert.Contains(kind, MakingGroup.Of(kind).Kinds));
        }

        [Fact]
        public void Of_NoSuchKind_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MakingGroup.Of((DerivedKind)999));
        }

        [Fact]
        public void InBackground_AreThumbnailsAndStreams_EachByAJobOfItsOwn()
        {
            Assert.Equal([MakingGroup.Background, MakingGroup.Stream], MakingGroup.InBackground);
            Assert.Equal([JobNames.DerivedMaking, JobNames.StreamMaking], MakingGroup.InBackground.Select(group => group.Start.Job));
        }

        /// <summary>A stream may take hours, so none is waited for.</summary>
        [Fact]
        public void Stream_IsMadeInTheLongLaneAndNeverWhenAsked()
        {
            Assert.Equal((PluginLane.Long, false), (MakingGroup.Stream.Lane, MakingGroup.Stream.Start.MadeWhenAsked));
            Assert.All(MakingGroup.All.Where(group => group != MakingGroup.Stream), group => Assert.Equal((PluginLane.Quick, true), (group.Lane, group.Start.MadeWhenAsked)));
        }
    }
}
