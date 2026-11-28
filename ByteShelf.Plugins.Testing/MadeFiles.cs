using ByteShelfCommon;

namespace ByteShelf.Plugins.Testing
{
    /// <summary>What a maker made of contents, kept in memory, as the server takes it: the kinds asked for, once each, within their sizes.</summary>
    public sealed class MadeFiles : IMadeFiles
    {
        private readonly IReadOnlyList<DerivedKind> _asked;
        private readonly Dictionary<DerivedKind, (string MediaType, byte[] Bytes)> _files = [];

        private MadeFiles(IReadOnlyList<DerivedKind> asked)
        {
            _asked = asked;
        }

        public IEnumerable<DerivedKind> Kinds => _files.Keys.Order();

        /// <summary>Has <paramref name="maker"/> make <paramref name="kinds"/> from <paramref name="contents"/>.</summary>
        public static async Task<MadeFiles> MakeAsync(IDerivedFileMaker maker, Stream contents, IReadOnlyList<DerivedKind> kinds)
        {
            ArgumentNullException.ThrowIfNull(maker);
            MadeFiles made = new MadeFiles(kinds);
            await maker.MakeAsync(contents, kinds, made);
            return made;
        }

        public async Task AddAsync(DerivedKind kind, string mediaType, Func<Stream, Task> write)
        {
            ArgumentNullException.ThrowIfNull(write);
            if (!_asked.Contains(kind))
            {
                throw new ArgumentException($"The kind {kind} was not asked for.", nameof(kind));
            }

            if (_files.ContainsKey(kind))
            {
                throw new InvalidOperationException($"The kind {kind} is made already.");
            }

            using LimitedStream file = new LimitedStream(DerivedFileLimits.MaxBytesOf(kind));
            await write(file);
            _files.Add(kind, (mediaType, file.ToArray()));
        }

        public string MediaTypeOf(DerivedKind kind)
        {
            return _files[kind].MediaType;
        }

        public byte[] BytesOf(DerivedKind kind)
        {
            return _files[kind].Bytes;
        }
    }
}
