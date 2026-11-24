using System.Text;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Storage.Models;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;
using Microsoft.Extensions.DependencyInjection;

namespace ByteShelf.Server.Tests.Derived
{
    /// <summary>The server's domains with one plugin that makes every kind from text files, whose runs are real child processes.</summary>
    public abstract class DerivedTestBase : ServiceTestBase
    {
        /// <summary>How long anything may take, as what a test waits for may never end: its clock stands still.</summary>
        protected static readonly TimeSpan RealTimeLimit = TimeSpan.FromSeconds(60);

        protected TestDerivedPlugins Plugins { get; } = new TestDerivedPlugins();

        protected IDerivedFileService DerivedFiles => Services.GetRequiredService<IDerivedFileService>();

        internal DerivedStore Store => Services.GetRequiredService<DerivedStore>();

        internal PluginProcesses Processes => Services.GetRequiredService<PluginProcesses>();

        internal ContentsLocks Locks => Services.GetRequiredService<ContentsLocks>();

        internal DerivedMaking Making => Services.GetRequiredService<DerivedMaking>();

        public override async Task InitializeAsync()
        {
            Plugins.All = [Plugins.Plugin()];
            await base.InitializeAsync();
        }

        public override async Task DisposeAsync()
        {
            await base.DisposeAsync();
            Plugins.Dispose();
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
            Plugins.AddTo(services);
        }

        /// <summary>Has the plugin make <paramref name="kind"/> from the file's contents.</summary>
        internal Task<MakingOutcome> MakeAsync(ShelfFile file, DerivedKind kind = DerivedKind.Thumbnail, PluginPriority priority = PluginPriority.Backlog)
        {
            return Making.MakeAsync(file.Sha256, kind, Plugins.All[0], priority).WaitAsync(RealTimeLimit);
        }

        /// <summary>The derived file's text and media type, asked for as a request does, in a scope of its own.</summary>
        protected async Task<string> GetAsync(Shelf shelf, ShelfFile file, DerivedKind kind = DerivedKind.Thumbnail)
        {
            await using AsyncServiceScope scope = Services.CreateAsyncScope();
            await using DerivedFile derived = await scope.ServiceProvider.GetRequiredService<IDerivedFileService>()
                .GetAsync(shelf, EntryReference.ForId(file.Id), kind, CancellationToken.None);
            Assert.Equal((kind, file.Sha256, TestMaker.MediaType), (derived.Kind, derived.Sha256, derived.MediaType));
            using StreamReader reader = new StreamReader(derived.Contents, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }

        /// <summary>Everything on disk under <c>derived/</c>, what is being made and what a process was given included.</summary>
        protected IReadOnlyList<string> AllDerivedFiles()
        {
            return Directory.Exists(TestRoot.Root.DerivedPath)
                ? Directory.EnumerateFiles(TestRoot.Root.DerivedPath, "*", SearchOption.AllDirectories).ToList()
                : [];
        }

        /// <summary>What is kept of which contents, made or failed, without anything else on disk.</summary>
        protected void AssertKept(params (DerivedKind Kind, ShelfFile Of)[] expected)
        {
            Assert.Equal(
                expected.Select(kept => (kept.Kind, kept.Of.Sha256)).Order(),
                DerivedKinds.All.SelectMany(kind => Store.EnumerateHashes(kind).Select(sha256 => (kind, sha256))).Order());
            Assert.Equal(
                expected.Sum(kept => Store.Find(kept.Kind, kept.Of.Sha256)?.Made == null ? 1 : 2),
                AllDerivedFiles().Count(path => !path.EndsWith(WorkFolder.ContentsSuffix, StringComparison.Ordinal)));
        }

        /// <summary>A <see cref="DerivedKind.Full"/> file as large as they may be together, which takes no disk.</summary>
        protected void PutLargeFull(ShelfFile file)
        {
            string source = Path.Combine(TestRoot.Root.StagingPath, Guid.NewGuid().ToString("N"));
            using (FileStream large = File.Create(source))
            {
                large.SetLength(DerivedStore.MaxFullBytes);
            }

            Store.Put(file.Sha256, new KeptFile(DerivedKind.Full, new DerivedEntry(TestMaker.Spec.DefaultId, 1, new MadeInfo(TestMaker.MediaType)), source));
        }

        /// <summary>One pass over what is already stored, as the job runs it.</summary>
        protected async Task<int> MakeBacklogAsync()
        {
            await using AsyncServiceScope scope = Services.CreateAsyncScope();
            int made = 0;
            foreach (DerivedFilePlugin plugin in Plugins.All)
            {
                made += await scope.ServiceProvider.GetRequiredService<DerivedBackground>().MakeBacklogAsync(plugin, CancellationToken.None);
            }

            return made;
        }
    }
}
