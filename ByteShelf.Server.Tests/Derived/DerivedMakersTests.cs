using ByteShelf.Server.Derived.Repositories;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Storage.Repositories;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Derived
{
    /// <summary>Which of several plugins makes a kind.</summary>
    public sealed class DerivedMakersTests : IAsyncLifetime
    {
        private readonly TestDerivedPlugins _plugins = new TestDerivedPlugins();
        private readonly TestRoot _root = TestRoot.Create();
        private readonly DerivedStore _store;

        public DerivedMakersTests()
        {
            _store = new DerivedStore(_root.Root, new ContentsLocks(), new DerivedChanges(), TimeProvider.System);
        }

        [Theory]
        [InlineData("image/png", "exact")]
        [InlineData("image/webp", "family-a")]
        [InlineData("text/plain", null)]
        public void For_AnExactTypeBeatsAFamilyThenTheFirstById(string mediaType, string? expected)
        {
            Install(Maker("family-b", "image/*"), Maker("exact", "image/png"), Maker("family-a", "image/*"));

            Assert.Equal(expected, For(mediaType));
        }

        [Fact]
        public void For_ContentsHeldAsSeveralTypes_IsOneMakerWhicheverOrderTheyComeIn()
        {
            Install(Maker("a", "image/*"), Maker("b", "video/mp4"));

            Assert.Equal(("b", "b"), (For("image/png", "video/mp4"), For("video/mp4", "image/png")));
            Assert.Null(For());
        }

        [Fact]
        public void For_AKindThePluginDoesNotMake_IsNone()
        {
            Install(_plugins.Plugin("a", 1, [DerivedKind.Thumbnail]));
            DerivedMakers makers = new DerivedMakers(_plugins, _store);

            Assert.Null(makers.For(DerivedKind.Preview, ["text/plain"]));
            Assert.True(makers.Makes(DerivedKind.Thumbnail, "a"));
            Assert.False(makers.Makes(DerivedKind.Preview, "a"));
            Assert.False(makers.Makes(DerivedKind.Thumbnail, "b"));
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            _plugins.Dispose();
            await _root.DisposeAsync();
        }

        private DerivedFilePlugin Maker(string id, string mediaType)
        {
            return _plugins.Plugin(id, 1, [DerivedKind.Thumbnail], mediaType);
        }

        private void Install(params DerivedFilePlugin[] plugins)
        {
            _plugins.All = plugins;
        }

        private string? For(params string[] mediaTypes)
        {
            return new DerivedMakers(_plugins, _store).For(DerivedKind.Thumbnail, mediaTypes)?.Plugin.Id;
        }
    }
}
