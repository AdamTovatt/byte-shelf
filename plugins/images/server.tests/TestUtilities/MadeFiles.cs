using ByteShelfCommon;
using ImageMagick;

namespace ByteShelf.Plugins.Images.Tests.TestUtilities
{
    /// <summary>What a maker made of contents, kept in memory, as the server takes it: the kinds asked for, once each, within their sizes.</summary>
    internal sealed class MadeFiles : IMadeFiles
    {
        private readonly IReadOnlyList<DerivedKind> _asked;
        private readonly Dictionary<DerivedKind, (string MediaType, byte[] Bytes)> _files = [];

        private MadeFiles(IReadOnlyList<DerivedKind> asked)
        {
            _asked = asked;
        }

        /// <summary>Every kind there is.</summary>
        public static IReadOnlyList<DerivedKind> AllKinds { get; } = Enum.GetValues<DerivedKind>();

        public IEnumerable<DerivedKind> Kinds => _files.Keys.Order();

        /// <summary>Makes <paramref name="kinds"/> from <paramref name="contents"/>, or every kind when there are none.</summary>
        public static async Task<MadeFiles> MakeAsync(byte[] contents, params IReadOnlyList<DerivedKind> kinds)
        {
            using MemoryStream stream = new MemoryStream(contents, writable: false);
            return await MakeAsync(stream, kinds);
        }

        public static async Task<MadeFiles> MakeAsync(Stream contents, params IReadOnlyList<DerivedKind> kinds)
        {
            MadeFiles made = new MadeFiles(kinds.Count == 0 ? AllKinds : kinds);
            await new ImageMaker().MakeAsync(contents, made._asked, made);
            return made;
        }

        /// <summary>Makes <paramref name="kind"/> from <paramref name="contents"/>, and decodes it.</summary>
        public static async Task<MagickImage> MakeOneAsync(byte[] contents, DerivedKind kind)
        {
            return (await MakeAsync(contents, kind)).Decode(kind);
        }

        public async Task AddAsync(DerivedKind kind, string mediaType, Func<Stream, Task> write)
        {
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

        /// <summary>The file of <paramref name="kind"/> as the type it says it is.</summary>
        public MagickImage Decode(DerivedKind kind)
        {
            MagickFormat format = MediaTypeOf(kind) == "image/webp" ? MagickFormat.WebP : MagickFormat.Jpeg;
            return new MagickImage(BytesOf(kind), new MagickReadSettings { Format = format });
        }
    }
}
