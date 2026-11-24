using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ByteShelf.Server.Tests.Derived
{
    /// <summary>What a run of a child process comes to that does not do as one of the server's own does.</summary>
    public sealed class MakerRunsTests : DerivedTestBase
    {
        private MakerExecutable _program = null!;

        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Replace(ServiceDescriptor.Singleton(_ => _program));
        }

        [UnixFact]
        public async Task RunAsync_ACrashBeforeTheMakerRan_IsNotTheContentsFailure()
        {
            _program = ShellProgram.Of("kill -9 $$");
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "hello");

            await Assert.ThrowsAsync<MakerUnavailableException>(() => MakeAsync(file));

            Assert.Empty(AllDerivedFiles());
        }

        [UnixFact]
        public async Task RunAsync_ACrashOnceTheMakerRan_IsTheContentsFailure()
        {
            _program = ShellProgram.Of("touch started; kill -9 $$");
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "hello");

            Assert.Equal(MakingOutcome.Kept, await MakeAsync(file));

            Assert.Null(Store.Find(DerivedKind.Thumbnail, file.Sha256)!.Made);
        }

        [UnixFact]
        public async Task RunAsync_AFileThatIsNotThere_HasFailedAlone()
        {
            _program = ShellProgram.Of("""
                echo '[{"kind":"thumbnail","mediaType":"image/webp"},{"kind":"preview","mediaType":"image/webp"}]' > made.json
                echo made > preview
                """);
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "hello");

            Assert.Equal(MakingOutcome.Kept, await MakeAsync(file));

            Assert.Null(Store.Find(DerivedKind.Thumbnail, file.Sha256)!.Made);
            Assert.NotNull(Store.Find(DerivedKind.Preview, file.Sha256)!.Made);
        }

        [UnixFact]
        public async Task RunAsync_WhatIsNotAMediaType_HasFailedAlone()
        {
            _program = ShellProgram.Of("""
                echo '[{"kind":"thumbnail","mediaType":"not a type"},{"kind":"preview","mediaType":"image/webp"}]' > made.json
                echo made > preview; echo made > thumbnail
                """);
            ShelfFile file = await StoreTextAsync(await CreateShelfAsync(), "/a.txt", "hello");

            Assert.Equal(MakingOutcome.Kept, await MakeAsync(file));

            Assert.Null(Store.Find(DerivedKind.Thumbnail, file.Sha256)!.Made);
            Assert.NotNull(Store.Find(DerivedKind.Preview, file.Sha256)!.Made);
        }

        private Task<MakingOutcome> MakeAsync(ShelfFile file)
        {
            return Making.MakeAsync(file.Sha256, DerivedKind.Thumbnail, Plugins.All[0], MakingPriority.Backlog);
        }
    }
}
