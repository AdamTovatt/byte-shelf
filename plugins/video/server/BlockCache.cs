using System.Diagnostics.CodeAnalysis;

namespace ByteShelf.Plugins.Video
{
    /// <summary>
    /// Reads of seekable contents at any place, one at a time, by blocks, keeping the last ones read: what each ffmpeg
    /// that seeks reads again, such as an MP4's index, is read of the contents once.
    /// </summary>
    internal sealed class BlockCache : IDisposable
    {
        /// <summary>Little more than ffmpeg needs at a seek, of a keyframe and what is around it.</summary>
        internal const int BlockBytes = 64 * 1024;

        /// <summary>16 MiB, an index's size of a film of hours.</summary>
        internal const int KeptBlocks = 256;

        [SuppressMessage("Reliability", "CA2213:Disposable fields should be disposed", Justification = "Whoever gave it disposes it.")]
        private readonly Stream _contents;
        private readonly SemaphoreSlim _reading = new SemaphoreSlim(1, 1);
        private readonly Dictionary<long, LinkedListNode<(long Index, byte[] Bytes, int Length)>> _kept = [];
        private readonly LinkedList<(long Index, byte[] Bytes, int Length)> _recent = new LinkedList<(long Index, byte[] Bytes, int Length)>();

        public BlockCache(Stream contents)
        {
            _contents = contents;
            Length = contents.Length;
        }

        public long Length { get; }

        public void Dispose()
        {
            _reading.Dispose();
        }

        /// <summary>Some of the contents at <paramref name="position"/>, within one block; none at their end.</summary>
        public async Task<int> ReadAsync(long position, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            if (position >= Length || buffer.Length == 0)
            {
                return 0;
            }

            long index = position / BlockBytes;
            int offset = (int)(position - (index * BlockBytes));
            await _reading.WaitAsync(cancellationToken);
            try
            {
                (long _, byte[] bytes, int length) = await BlockAsync(index, cancellationToken);
                int count = Math.Max(0, Math.Min(buffer.Length, length - offset));
                bytes.AsMemory(offset, count).CopyTo(buffer);
                return count;
            }
            finally
            {
                _reading.Release();
            }
        }

        private async Task<(long Index, byte[] Bytes, int Length)> BlockAsync(long index, CancellationToken cancellationToken)
        {
            if (_kept.TryGetValue(index, out LinkedListNode<(long Index, byte[] Bytes, int Length)>? kept))
            {
                _recent.Remove(kept);
                _recent.AddFirst(kept);
                return kept.Value;
            }

            byte[] bytes = _recent.Count >= KeptBlocks ? Forget() : new byte[BlockBytes];
            _contents.Position = index * BlockBytes;
            int length = 0;
            int read;
            while (length < bytes.Length && (read = await _contents.ReadAsync(bytes.AsMemory(length), cancellationToken)) > 0)
            {
                length += read;
            }

            LinkedListNode<(long Index, byte[] Bytes, int Length)> block = _recent.AddFirst((index, bytes, length));
            _kept.Add(index, block);
            return block.Value;
        }

        /// <summary>Forgets the block read longest ago, whose buffer it answers.</summary>
        private byte[] Forget()
        {
            LinkedListNode<(long Index, byte[] Bytes, int Length)> oldest = _recent.Last!;
            _recent.RemoveLast();
            _kept.Remove(oldest.Value.Index);
            return oldest.Value.Bytes;
        }
    }
}
