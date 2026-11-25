using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Storage.Services;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.Derived
{
    /// <summary>What is read from disk once, and what is read again.</summary>
    public sealed class KnownEntriesTests
    {
        private readonly ContentsLocks _locks = new ContentsLocks();
        private int _reads;

        [Fact]
        public void Get_AskedAgain_IsNotReadAgain()
        {
            KnownEntries known = new KnownEntries(_locks, 10);

            IReadOnlyDictionary<DerivedKind, DerivedEntry> first = Get(known, "a");

            Assert.Same(first, Get(known, "a"));
            Assert.Equal(1, _reads);
        }

        [Fact]
        public void Forget_HasThatOneAloneReadAgain()
        {
            KnownEntries known = new KnownEntries(_locks, 10);
            Get(known, "a");
            Get(known, "b");

            known.Forget("a");

            Get(known, "b");
            Assert.Equal(2, _reads);
            Get(known, "a");
            Assert.Equal(3, _reads);
        }

        [Fact]
        public async Task Get_AsSomebodyMayChangeIt_IsReadAgainAfter()
        {
            KnownEntries known = new KnownEntries(_locks, 10);
            using (await _locks.LockAsync("a"))
            {
                Get(known, "a");
            }

            Get(known, "a");
            Get(known, "a");

            Assert.Equal(2, _reads);
        }

        [Fact]
        public void Get_OneMoreThanTheMost_ForgetsTheOthers()
        {
            KnownEntries known = new KnownEntries(_locks, 2);
            Get(known, "a");
            Get(known, "b");
            Get(known, "a");
            Assert.Equal(2, _reads);

            Get(known, "c");
            Get(known, "c");
            Assert.Equal(3, _reads);

            Get(known, "a");
            Assert.Equal(4, _reads);
        }

        private IReadOnlyDictionary<DerivedKind, DerivedEntry> Get(KnownEntries known, string sha256)
        {
            return known.Get(sha256, () =>
            {
                _reads++;
                return new Dictionary<DerivedKind, DerivedEntry>();
            });
        }
    }
}
