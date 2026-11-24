using ByteShelf.Plugins;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Tests.TestUtilities;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Derived
{
    /// <summary>Derived files on disk.</summary>
    public sealed class DerivedStoreTests : IAsyncLifetime
    {
        private static readonly string Sha256 = TestHashes.Sha256Of("contents");
        private static readonly DerivedEntry Made = new DerivedEntry("images", 3, new MadeInfo("image/webp", new DerivedOriginal(4000, 3000, "image/jpeg")));
        private static readonly DerivedEntry Failed = new DerivedEntry("images", 3, null);

        private readonly TestClock _clock = new TestClock(TestInstants.Now);
        private TestRoot _root = null!;
        private readonly ContentsLocks _locks = new ContentsLocks();
        private readonly DerivedChanges _changes = new DerivedChanges();
        private DerivedStore _store = null!;

        public Task InitializeAsync()
        {
            _root = TestRoot.Create();
            _store = new DerivedStore(_root.Root, _locks, _changes, _clock);
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            await _root.DisposeAsync();
        }

        [Fact]
        public async Task Put_KeepsTheFileAndWhatIsKnownOfItUnderItsKindAndHash()
        {
            Put(DerivedKind.Thumbnail, Sha256, Made, "small");

            string path = Path.Combine(_root.Root.Path, "derived", "thumbnail", Sha256[..2], Sha256[2..4], Sha256);
            Assert.Equal("small", await File.ReadAllTextAsync(path));
            Assert.Equal(
                "{\"plugin\":\"images\",\"version\":3,\"made\":{\"mediaType\":\"image/webp\",\"original\":{\"width\":4000,\"height\":3000,\"mediaType\":\"image/jpeg\"}}}",
                await File.ReadAllTextAsync(path + ".json"));
            Assert.Equal(2, AllFiles().Count);
            Assert.Equal(Made, _store.Find(DerivedKind.Thumbnail, Sha256));
            Assert.Null(_store.Find(DerivedKind.Preview, Sha256));
            Assert.Null(_store.TryOpenRead(DerivedKind.Preview, Sha256));
        }

        [Fact]
        public void Put_AFailure_TakesThePlaceOfWhatWasMadeAndIsTakenByIt()
        {
            Put(DerivedKind.Thumbnail, Sha256, Made, "small");

            _store.Put(Sha256, new KeptFile(DerivedKind.Thumbnail, Failed, SourcePath: null));
            Assert.Equal(Failed, _store.Find(DerivedKind.Thumbnail, Sha256));
            Assert.Single(AllFiles());

            Put(DerivedKind.Thumbnail, Sha256, Made, "small");
            Assert.Equal(Made, _store.Find(DerivedKind.Thumbnail, Sha256));
        }

        [Fact]
        public void Find_PartOfWhatWasKept_IsNothing()
        {
            Put(DerivedKind.Thumbnail, Sha256, Made, "small");
            string path = _store.GetPath(DerivedKind.Thumbnail, Sha256);

            File.Move(path, path + ".away");
            Assert.Null(_store.Find(DerivedKind.Thumbnail, Sha256));
            File.Move(path + ".away", path);
            File.WriteAllText(path + ".json", "{\"plugin\":");
            Assert.Null(_store.Find(DerivedKind.Thumbnail, Sha256));
            File.Delete(path + ".json");
            Assert.Null(_store.Find(DerivedKind.Thumbnail, Sha256));
        }

        [UnixFact]
        public void Find_AnEntryThatCannotBeRead_IsNothing()
        {
            Put(DerivedKind.Thumbnail, Sha256, Made, "small");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(_store.GetPath(DerivedKind.Thumbnail, Sha256) + ".json", UnixFileMode.None);
            }

            Assert.Null(_store.Find(DerivedKind.Thumbnail, Sha256));
        }

        [Fact]
        public void FindAll_OnceItWasChanged_SaysSo()
        {
            Assert.Empty(_store.FindAll(Sha256));
            Put(DerivedKind.Thumbnail, Sha256, Made, "small");
            Assert.Equal([DerivedKind.Thumbnail], _store.FindAll(Sha256).Keys);
            Put(DerivedKind.Preview, Sha256, Failed, "large");
            Assert.Equal(2, _store.FindAll(Sha256).Count);

            _store.Delete(DerivedKind.Thumbnail, Sha256);
            Assert.Equal([DerivedKind.Preview], _store.FindAll(Sha256).Keys);
            _store.Delete(Sha256);
            Assert.Empty(_store.FindAll(Sha256));
        }

        [Fact]
        public void WhatCameOrWent_IsAChange()
        {
            Put(DerivedKind.Thumbnail, Sha256, Made, "small");
            Assert.Equal([Sha256], _changes.Take());

            _store.Put(Sha256, new KeptFile(DerivedKind.Thumbnail, Failed, SourcePath: null));
            Assert.Equal([Sha256], _changes.Take());

            Put(DerivedKind.Full, Sha256, Made, "full");
            _changes.Take();
            _store.Delete(DerivedKind.Full, Sha256);
            Assert.Equal([Sha256], _changes.Take());

            Put(DerivedKind.Agent, Sha256, Made, "agent");
            _changes.Take();
            _store.Delete(Sha256);
            Assert.Equal([Sha256], _changes.Take());
        }

        [Fact]
        public void WhatWasNeverThere_IsNoChange()
        {
            _store.Put(Sha256, new KeptFile(DerivedKind.Thumbnail, Failed, SourcePath: null));
            _store.Delete(DerivedKind.Preview, Sha256);
            _store.Delete(Sha256);

            Assert.Empty(_changes.Take());
        }

        [Fact]
        public void Put_ThatFailsAfterOne_IsAChange()
        {
            string source = Path.Combine(_root.Root.StagingPath, "small");
            File.WriteAllText(source, "small");

            Assert.ThrowsAny<IOException>(() => _store.Put(
                Sha256, new KeptFile(DerivedKind.Thumbnail, Made, source), new KeptFile(DerivedKind.Preview, Made, Path.Combine(_root.Root.StagingPath, "gone"))));

            Assert.Equal([Sha256], _changes.Take());
        }

        [Fact]
        public void Delete_RemovesEveryKindOfTheContentsAlone()
        {
            string other = TestHashes.Sha256Of("other");
            foreach (DerivedKind kind in DerivedKinds.All)
            {
                Put(kind, Sha256, Made, "made");
            }

            Put(DerivedKind.Thumbnail, other, Made, "made");

            _store.Delete(Sha256);

            Assert.Equal([other, other + ".json"], AllFiles().Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal));
        }

        [Fact]
        public void EnumerateHashes_HasWhatACrashLeftOfOneToo()
        {
            string left = TestHashes.Sha256Of("left");
            Put(DerivedKind.Thumbnail, Sha256, Made, "made");
            Put(DerivedKind.Thumbnail, left, Made, "made");
            File.Delete(_store.GetPath(DerivedKind.Thumbnail, left) + ".json");

            Assert.Equal(new[] { Sha256, left }.Order(StringComparer.Ordinal), _store.EnumerateHashes(DerivedKind.Thumbnail).Order(StringComparer.Ordinal));
            Assert.Empty(_store.EnumerateHashes(DerivedKind.Preview));
        }

        [Fact]
        public async Task TrimFull_OverTheLimit_RemovesTheLeastRecentlyUsed()
        {
            string[] hashes = ["a", "b", "c", "d"];
            hashes = hashes.Select(TestHashes.Sha256Of).ToArray();
            foreach (string sha256 in hashes)
            {
                PutSized(sha256, DerivedStore.MaxFullBytes / 3);
                _clock.Advance(TimeSpan.FromMinutes(1));
            }

            // The oldest is used again, so the two after it are the least recently used.
            await using (_store.TryOpenRead(DerivedKind.Full, hashes[0]))
            {
            }

            Assert.NotEmpty(_store.FindAll(hashes[1]));
            _changes.Take();
            int removed = _store.TrimFull();

            Assert.Equal(1, removed);
            Assert.Empty(_store.FindAll(hashes[1]));
            Assert.Contains(hashes[1], _changes.Take());
            Assert.Equal(new[] { hashes[0], hashes[2], hashes[3] }.Order(StringComparer.Ordinal), _store.EnumerateHashes(DerivedKind.Full).Order(StringComparer.Ordinal));
            Assert.Equal(0, _store.TrimFull());
        }

        [Fact]
        public async Task TrimFull_OneOverTheLimitByItself_StaysUntilAnotherIsUsed()
        {
            string large = TestHashes.Sha256Of("large");
            PutSized(large, DerivedStore.MaxFullBytes + 1);
            Assert.Equal(0, _store.TrimFull());

            _clock.Advance(TimeSpan.FromMinutes(1));
            PutSized(Sha256, 1);
            _store.TrimFull();

            Assert.Equal([Sha256], _store.EnumerateHashes(DerivedKind.Full));
        }

        [Fact]
        public async Task TrimFull_OneWhoseLockIsHeld_IsPassedOverWithoutWaiting()
        {
            string held = TestHashes.Sha256Of("held");
            PutSized(held, DerivedStore.MaxFullBytes);
            _clock.Advance(TimeSpan.FromMinutes(1));
            PutSized(Sha256, 1);

            using (await _locks.LockAsync(held))
            {
                Assert.Equal(0, _store.TrimFull());
            }

            Assert.Equal(1, _store.TrimFull());
            Assert.Equal([Sha256], _store.EnumerateHashes(DerivedKind.Full));
        }

        private void Put(DerivedKind kind, string sha256, DerivedEntry entry, string text)
        {
            string source = Path.Combine(_root.Root.StagingPath, Guid.NewGuid().ToString("N"));
            File.WriteAllText(source, text);
            _store.Put(sha256, new KeptFile(kind, entry, source));
        }

        // A sparse file, so it takes no disk.
        private void PutSized(string sha256, long bytes)
        {
            string source = Path.Combine(_root.Root.StagingPath, Guid.NewGuid().ToString("N"));
            using (FileStream file = File.Create(source))
            {
                file.SetLength(bytes);
            }

            _store.Put(sha256, new KeptFile(DerivedKind.Full, Made, source));
        }

        private List<string> AllFiles()
        {
            return Directory.EnumerateFiles(_root.Root.DerivedPath, "*", SearchOption.AllDirectories).ToList();
        }
    }
}
