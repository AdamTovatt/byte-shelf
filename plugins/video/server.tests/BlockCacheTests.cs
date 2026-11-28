using ByteShelf.Plugins.Video.Tests.TestUtilities;

namespace ByteShelf.Plugins.Video.Tests
{
    public sealed class BlockCacheTests
    {
        private static readonly byte[] Contents = [.. Enumerable.Range(0, (3 * BlockCache.BlockBytes) + 100).Select(index => (byte)(index % 253))];

        [Fact]
        public async Task ReadAsync_AnyPlace_ReadsTheContentsThereWithinItsBlock()
        {
            using BlockCache cache = new BlockCache(new MemoryStream(Contents));
            byte[] buffer = new byte[BlockCache.BlockBytes];

            int read = await cache.ReadAsync(BlockCache.BlockBytes - 10, buffer, CancellationToken.None);

            Assert.Equal(10, read);
            Assert.Equal(Contents[(BlockCache.BlockBytes - 10)..BlockCache.BlockBytes], buffer[..10]);
        }

        [Fact]
        public async Task ReadAsync_TheLastBlock_IsAsLongAsTheRest()
        {
            using BlockCache cache = new BlockCache(new MemoryStream(Contents));
            byte[] buffer = new byte[1000];

            Assert.Equal(100, await cache.ReadAsync(3 * BlockCache.BlockBytes, buffer, CancellationToken.None));
            Assert.Equal(0, await cache.ReadAsync(Contents.Length, buffer, CancellationToken.None));
        }

        [Fact]
        public async Task ReadAsync_ABlockReadAgain_IsNotReadOfTheContents()
        {
            using CountingStream contents = new CountingStream(Contents);
            using BlockCache cache = new BlockCache(contents);
            byte[] buffer = new byte[10];

            await cache.ReadAsync(5, buffer, CancellationToken.None);
            await cache.ReadAsync(BlockCache.BlockBytes + 5, buffer, CancellationToken.None);
            await cache.ReadAsync(20, buffer, CancellationToken.None);

            Assert.Equal(2 * BlockCache.BlockBytes, contents.BytesRead);
        }

        [Fact]
        public async Task ReadAsync_MoreBlocksThanItKeeps_ForgetsTheOneReadLongestAgo()
        {
            byte[] large = new byte[(BlockCache.KeptBlocks + 1) * BlockCache.BlockBytes];
            using CountingStream contents = new CountingStream(large);
            using BlockCache cache = new BlockCache(contents);
            byte[] buffer = new byte[1];
            for (int block = 0; block <= BlockCache.KeptBlocks; block++)
            {
                await cache.ReadAsync((long)block * BlockCache.BlockBytes, buffer, CancellationToken.None);
            }

            long before = contents.BytesRead;
            await cache.ReadAsync(BlockCache.KeptBlocks * BlockCache.BlockBytes, buffer, CancellationToken.None);
            await cache.ReadAsync(0, buffer, CancellationToken.None);

            Assert.Equal(before + BlockCache.BlockBytes, contents.BytesRead);
        }
    }
}
