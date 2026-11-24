using System.Collections.Concurrent;
using ByteShelf.Plugins;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Plugins.Processes;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>The files a maker makes in one run, each written to the run's folder under its kind's name.</summary>
    internal sealed class MadeFiles : IMadeFiles
    {
        private readonly string _folder;
        private readonly IReadOnlyList<DerivedKind> _kinds;
        private readonly ConcurrentDictionary<DerivedKind, MadeInFolder> _made = new ConcurrentDictionary<DerivedKind, MadeInFolder>();
        private volatile Exception? _failure;

        public MadeFiles(string folder, IReadOnlyList<DerivedKind> kinds)
        {
            _folder = folder;
            _kinds = kinds;
        }

        public IReadOnlyList<MadeInFolder> Made => _made.Values.ToList();

        public async Task AddAsync(DerivedKind kind, string mediaType, DerivedOriginal? original, Func<Stream, Task> write)
        {
            if (!_kinds.Contains(kind))
            {
                throw new ArgumentException($"The kind {kind} was not asked for.", nameof(kind));
            }

            if (_made.ContainsKey(kind))
            {
                throw new InvalidOperationException($"The kind {kind} is made already.");
            }

            await using (Stream file = Open(Path.Combine(_folder, DerivedKinds.NameOf(kind)), DerivedFileLimits.MaxBytesOf(kind)))
            {
                await write(file);
            }

            _made[kind] = new MadeInFolder(kind, mediaType, original);
        }

        /// <exception cref="PluginUnavailableException">
        /// A file could not be written, for a full disk, say, which is not the contents' doing, whatever the maker made
        /// of it.
        /// </exception>
        public void ThrowIfNotWritten()
        {
            if (_failure != null)
            {
                throw new PluginUnavailableException("what it made could not be written", _failure);
            }
        }

        private MadeFileStream Open(string path, long maxBytes)
        {
            try
            {
                return new MadeFileStream(new FileStream(path, FileMode.Create, FileAccess.ReadWrite), maxBytes, this);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _failure = exception;
                throw;
            }
        }

        /// <summary>One of the files, no larger than it may be.</summary>
        private sealed class MadeFileStream : Stream
        {
            private readonly FileStream _file;
            private readonly long _maxBytes;
            private readonly MadeFiles _files;

            public MadeFileStream(FileStream file, long maxBytes, MadeFiles files)
            {
                _file = file;
                _maxBytes = maxBytes;
                _files = files;
            }

            public override bool CanRead => _file.CanRead;

            public override bool CanSeek => _file.CanSeek;

            public override bool CanWrite => _file.CanWrite;

            public override long Length => _file.Length;

            public override long Position
            {
                get => _file.Position;
                set => _file.Position = value;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return _file.Read(buffer, offset, count);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                return _file.Seek(offset, origin);
            }

            public override void SetLength(long value)
            {
                Reach(value);
                Write(() => _file.SetLength(value));
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                Reach(_file.Position + count);
                Write(() => _file.Write(buffer, offset, count));
            }

            public override void Flush()
            {
                Write(_file.Flush);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Write(_file.Dispose);
                }

                base.Dispose(disposing);
            }

            private void Reach(long length)
            {
                if (length > _maxBytes)
                {
                    throw new IOException($"A file of its kind may take {_maxBytes} bytes.");
                }
            }

            private void Write(Action write)
            {
                try
                {
                    write();
                }
                catch (IOException exception)
                {
                    _files._failure = exception;
                    throw;
                }
            }
        }
    }
}
