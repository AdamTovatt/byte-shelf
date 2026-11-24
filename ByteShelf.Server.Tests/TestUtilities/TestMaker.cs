using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Metadata.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>
    /// A plugin's maker of derived files that does, in its child process, what the contents say, as text:
    /// <c>hang</c>, <c>throw</c>, <c>crash</c>, <c>noise</c> (more output than a pipe holds), <c>temporary</c>
    /// (leaves a temporary file, and says in the log where), <c>without preview</c>, <c>large thumbnail</c> (one byte
    /// over the limit), <c>other kinds</c> (every kind, and each once more), <c>full disk</c> (as a disk that is full
    /// throws), <c>in the way</c> (what it makes cannot be written), <c>undeletable</c> (leaves a folder that cannot be
    /// removed, with no file of its own), <c>child crash &lt;path&gt;</c> and <c>child wait &lt;path&gt;</c> (starts a process that
    /// goes on, says its id at path + ".child", and crashes or waits) or <c>wait &lt;path&gt;</c> (says so at
    /// path + ".started", with its process id, and goes on once there is a file at the path). Anything else, and
    /// those that go on, it makes into text that says the kind, the contents and the maker.
    /// </summary>
    public sealed class TestMaker : IDerivedFileMaker
    {
        public const string MediaType = "image/webp";

        private readonly Spec _spec;

        private TestMaker(Spec spec)
        {
            _spec = spec;
            Makes = spec.Kinds.Select(kind => new DerivedFileDeclaration(kind.Kind, MediaTypeSet.Of(kind.MediaTypes), kind.Version)).ToList();
        }

        public IReadOnlyList<DerivedFileDeclaration> Makes { get; }

        /// <summary>The plugin the server is given, whose maker a child process finds again by its location.</summary>
        public static DerivedFilePlugin Plugin(Spec spec)
        {
            return new DerivedFilePlugin(spec.Id, JsonSerializer.Serialize(spec), new TestMaker(spec).Makes);
        }

        /// <summary>The maker at a location, in a child process.</summary>
        public static IDerivedFileMaker? Load(string location)
        {
            return location.Length == 0 ? null : new TestMaker(JsonSerializer.Deserialize<Spec>(location)!);
        }

        /// <summary>What it makes of the contents <paramref name="text"/>.</summary>
        public static string Made(DerivedKind kind, string text, string plugin = Spec.DefaultId, int version = 1)
        {
            return $"{kind} of {text} by {plugin} {version}";
        }

        public async Task<IReadOnlyList<MadeFile>> MakeAsync(Stream contents, IReadOnlyList<DerivedKind> kinds, CancellationToken cancellationToken)
        {
            using StreamReader reader = new StreamReader(contents);
            string text = await reader.ReadToEndAsync(cancellationToken);
            await File.AppendAllTextAsync(_spec.LogPath, $"{text.ReplaceLineEndings(" ")}: {string.Join(", ", kinds)}\n", cancellationToken);

            switch (text.Split(' ')[0])
            {
                case "hang":
                    Thread.Sleep(Timeout.Infinite);
                    break;
                case "throw":
                    throw new InvalidDataException($"The contents say {text}");
                case "crash":
                    Environment.FailFast($"The contents say {text}");
                    break;
                case "noise":
                    Console.Out.Write(new string('x', 1_000_000));
                    Console.Error.Write(new string('x', 1_000_000));
                    break;
                case "temporary":
                    string temporary = Path.GetTempFileName();
                    await File.WriteAllTextAsync(temporary, text, cancellationToken);
                    await File.AppendAllTextAsync(_spec.LogPath, temporary + "\n", cancellationToken);
                    await File.WriteAllTextAsync("in-working-folder", text, cancellationToken);
                    break;
                case "full":
                    throw new IOException("The disk is full", OperatingSystem.IsWindows() ? unchecked((int)0x80070070) : 28);
                case "in":
                    foreach (DerivedKind kind in kinds)
                    {
                        Directory.CreateDirectory(DerivedKinds.NameOf(kind));
                    }

                    break;
                case "undeletable":
                    DirectoryInfo kept = Directory.CreateDirectory("undeletable");
                    await File.WriteAllTextAsync(Path.Combine(kept.FullName, "file"), text, cancellationToken);
                    if (!OperatingSystem.IsWindows())
                    {
                        File.SetUnixFileMode(kept.FullName, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                    }

                    break;
                case "child":
                    string[] child = text.Split(' ');
                    using (Process started = Process.Start("sleep", "1000"))
                    {
                        Say(child[2] + ".child", started.Id);
                    }

                    if (child[1] == "crash")
                    {
                        Environment.FailFast($"The contents say {text}");
                    }

                    await WaitAsync(child[2], cancellationToken);
                    break;
                case "wait":
                    await WaitAsync(text.Split(' ')[1], cancellationToken);
                    break;
            }

            if (text == "other kinds")
            {
                return DerivedKinds.All.Concat(DerivedKinds.All).Select((kind, index) => Make(kind, $"{index}")).ToList();
            }

            return kinds
                .Where(kind => text != "without preview" || kind != DerivedKind.Preview)
                .Select(kind => Make(kind, text == "large thumbnail" && kind == DerivedKind.Thumbnail
                    ? new string('x', (int)DerivedKinds.MaxBytesOf(kind) + 1)
                    : Made(kind, text, _spec.Id, Makes.First(made => made.Kind == kind).Version)))
                .ToList();
        }

        private static MadeFile Make(DerivedKind kind, string contents)
        {
            return new MadeFile(kind, MediaType, Encoding.UTF8.GetBytes(contents), new DerivedOriginal(40, 30, "image/png"));
        }

        private static async Task WaitAsync(string path, CancellationToken cancellationToken)
        {
            Say(path + ".started", Environment.ProcessId);
            while (!File.Exists(path))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken);
            }
        }

        /// <summary>Writes the id of a process to a file, whole from the start.</summary>
        private static void Say(string path, int processId)
        {
            File.WriteAllText(path + ".tmp", processId.ToString(CultureInfo.InvariantCulture));
            File.Move(path + ".tmp", path);
        }

        /// <param name="LogPath">The file each run writes a line to: the contents, then the kinds asked for.</param>
        public sealed record Spec(string Id, IReadOnlyList<KindSpec> Kinds, string LogPath)
        {
            public const string DefaultId = "test-maker";
        }

        public sealed record KindSpec(DerivedKind Kind, IReadOnlyList<string> MediaTypes, int Version);
    }
}
