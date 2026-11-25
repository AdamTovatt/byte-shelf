using ByteShelf.Plugins;
using ByteShelf.Server.Plugins.Services;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Plugins
{
    /// <summary>What a plugin may not register.</summary>
    public sealed class PluginBuilderTests
    {
        [Fact]
        public void AddMaker_AKindTwice_IsRefused()
        {
            PluginBuilder builder = new PluginBuilder(new ServiceCollection());
            DerivedFileDeclaration thumbnails = new DerivedFileDeclaration(DerivedKind.Thumbnail, 1, ["image/*"]);

            Assert.Throws<ArgumentException>(() => builder.AddMaker<Maker>(thumbnails, thumbnails with { Version = 2 }));
            Assert.Null(builder.Maker);
        }

        private sealed class Maker : IDerivedFileMaker
        {
            public Task MakeAsync(Stream contents, IReadOnlyList<DerivedKind> kinds, IMadeFiles made)
            {
                throw new NotSupportedException();
            }
        }
    }
}
