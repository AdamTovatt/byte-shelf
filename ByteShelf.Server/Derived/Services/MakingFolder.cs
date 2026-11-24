using System.Text.Json;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Shared.Json;
using ByteShelf.Server.Shared.Root;
using ByteShelfCommon;

namespace ByteShelf.Server.Derived.Services
{
    /// <summary>
    /// The folder one run of a maker works in, under <c>derived/work/</c>: what the server asks of the child
    /// process, what the child answers, and whatever temporary files it makes. Everything a run leaves on disk
    /// is in it, so removing it leaves nothing read from the contents, whose SHA-256 its name starts with: one
    /// that could not be removed is found by them, whichever server left it.
    /// </summary>
    internal sealed class MakingFolder
    {
        private const string WorkFolderName = "work";

        public MakingFolder(string path)
        {
            Path = path;
        }

        public string Path { get; }

        private string RequestPath => InFolder("request.json");

        private string StartedPath => InFolder("started");

        private string FailurePath => InFolder("failure.txt");

        private string MadePath => InFolder("made.json");

        /// <summary>A new one in <paramref name="root"/>, for a run over the contents <paramref name="sha256"/>.</summary>
        public static MakingFolder Create(RootFolder root, string sha256)
        {
            MakingFolder folder = new MakingFolder(System.IO.Path.Combine(AllIn(root), $"{sha256}.{Guid.NewGuid():N}"));
            Directory.CreateDirectory(folder.Path);
            return folder;
        }

        /// <summary>Every one in <paramref name="root"/>, or those of the contents <paramref name="sha256"/>.</summary>
        public static IReadOnlyList<MakingFolder> Find(RootFolder root, string? sha256 = null)
        {
            return Directory.Exists(AllIn(root))
                ? Directory.EnumerateDirectories(AllIn(root), sha256 == null ? "*" : sha256 + ".*").Select(path => new MakingFolder(path)).ToList()
                : [];
        }

        public string PathOf(DerivedKind kind)
        {
            return InFolder(DerivedKinds.NameOf(kind));
        }

        public void WriteRequest(MakingRequest request)
        {
            Write(RequestPath, request);
        }

        public MakingRequest? ReadRequest()
        {
            return Read<MakingRequest>(RequestPath);
        }

        /// <summary>Says that the plugin is loaded and from here on it is the contents that make it fail.</summary>
        public void SetStarted()
        {
            File.WriteAllText(StartedPath, string.Empty);
        }

        public bool IsStarted()
        {
            return File.Exists(StartedPath);
        }

        /// <summary>Says why the run failed, by the exception's type alone: a message may quote the contents.</summary>
        public void WriteFailure(Exception exception)
        {
            File.WriteAllText(FailurePath, exception.GetType().FullName);
        }

        public string? ReadFailure()
        {
            return File.Exists(FailurePath) ? File.ReadAllText(FailurePath) : null;
        }

        public void WriteMade(IReadOnlyList<MadeInFolder> made)
        {
            Write(MadePath, made);
        }

        /// <summary>What was made, each with its file at <see cref="PathOf"/>; null when that is not said as it should be.</summary>
        public IReadOnlyList<MadeInFolder>? ReadMade()
        {
            return Read<List<MadeInFolder>>(MadePath);
        }

        /// <exception cref="IOException">It could not be removed.</exception>
        /// <exception cref="UnauthorizedAccessException">It could not be removed.</exception>
        public void Remove()
        {
            Directory.Delete(Path, recursive: true);
        }

        private static string AllIn(RootFolder root)
        {
            return System.IO.Path.Combine(root.DerivedPath, WorkFolderName);
        }

        private static void Write<T>(string path, T value)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(value, JsonDefaults.CamelCase));
        }

        private static T? Read<T>(string path)
            where T : class
        {
            try
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonDefaults.CamelCase);
            }
            catch (Exception exception) when (exception is FileNotFoundException or JsonException)
            {
                return null;
            }
        }

        private string InFolder(string name)
        {
            return System.IO.Path.Combine(Path, name);
        }
    }

    /// <summary>What the server asks of a child process.</summary>
    /// <param name="Location">Where the plugin is (<see cref="DerivedFilePlugin.Location"/>).</param>
    /// <param name="ContentsPath">The file with the contents.</param>
    /// <param name="Kinds">What to make of them, each at the version the server knows: another plugin may be at the location by now.</param>
    internal sealed record MakingRequest(string Location, string ContentsPath, IReadOnlyList<RequestedKind> Kinds);

    internal sealed record RequestedKind(DerivedKind Kind, int Version);

    /// <summary>One kind a child process made.</summary>
    internal sealed record MadeInFolder(DerivedKind Kind, string MediaType, DerivedOriginal? Original);
}
