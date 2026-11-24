using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ByteShelf.Plugins;
using ByteShelf.Server.Derived.Models;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Plugins.Services;
using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>
    /// A plugin's maker of derived files that does, in its child process, what the contents say, as text:
    /// <c>hang</c>, <c>throw</c>, <c>crash</c> (<c>natively</c> too), <c>eat managed</c> or <c>native memory</c> (all it
    /// gets), <c>hoard</c> (more than it may keep), <c>print natively</c> (and read the standard input), <c>noise</c> (more output than a pipe holds),
    /// <c>temporary</c> (leaves a temporary file, and says in the log where), <c>without preview</c>, <c>large
    /// thumbnail</c> (one byte over the limit, whose refusal it lets pass), <c>other kinds</c> (a full too), <c>kinds
    /// twice</c> (the first once more), <c>full disk</c> (as a disk that is full throws), <c>filled disk</c> (one that is
    /// full as it makes the thumbnail, on Linux, which it lets pass), <c>in the way</c> (what it makes cannot be
    /// written), <c>large by path</c> (writes more to the thumbnail's path than it may be, once it is made),
    /// <c>linked</c> (puts a link where the thumbnail was), <c>reads on</c> (goes on reading as it answers),
    /// <c>undeletable</c> (leaves a folder that cannot be removed, with no file of its own),
    /// <c>child crash &lt;path&gt;</c> and <c>child wait &lt;path&gt;</c> (starts a process that goes on, says its id at
    /// path + ".child", and crashes or waits) or <c>wait &lt;path&gt;</c> (says so at path + ".started", with its process
    /// id, and goes on once there is a file at the path). What starts with <c>picture</c> it makes into a picture for
    /// agents. Anything else, and those that go on, it makes into text that says the kind, the contents and the maker.
    /// </summary>
    public sealed class TestMaker : IDerivedFileMaker
    {
        public const string MediaType = "image/webp";

        private readonly Spec _spec;

        private TestMaker(Spec spec)
        {
            _spec = spec;
        }

        /// <summary>The plugin the server is given, whose maker a child process finds again by its location.</summary>
        public static DerivedFilePlugin Plugin(Spec spec)
        {
            return new DerivedFilePlugin(new PluginTarget(spec.Id, JsonSerializer.Serialize(spec)), MakesOf(spec));
        }

        /// <summary>What its child process does.</summary>
        internal static PluginWork LoadWork(string location)
        {
            Spec spec = location.Length == 0 ? throw new FileNotFoundException("There is no plugin.") : JsonSerializer.Deserialize<Spec>(location)!;
            return PluginWorkLoader.Of(null, new PluginMakerRegistration(() => new TestMaker(spec), MakesOf(spec)));
        }

        /// <summary>What it makes of the contents <paramref name="text"/>.</summary>
        public static string Made(DerivedKind kind, string text, string plugin = Spec.DefaultId, int version = 1)
        {
            return $"{kind} of {text} by {plugin} {version}";
        }

        public async Task MakeAsync(Stream contents, IReadOnlyList<DerivedKind> kinds, IMadeFiles made)
        {
            using StreamReader reader = new StreamReader(contents, leaveOpen: true);
            string text = await reader.ReadToEndAsync();
            await File.AppendAllTextAsync(_spec.LogPath, $"{text.ReplaceLineEndings(" ")}: {string.Join(", ", kinds)}\n");

            switch (text.Split(' ')[0])
            {
                case "hang":
                    if (text == "hang natively")
                    {
                        _ = NativeMethods.Pause();
                    }

                    Thread.Sleep(Timeout.Infinite);
                    break;
                case "eat":
                    Eat(native: text == "eat native memory");
                    break;
                case "hoard":
                    _ = Take((int)PluginMemory.MaxKeptBytes, native: true);
                    break;
                case "print":
                    _ = NativeMethods.Write(1, "printed", 7);
                    _ = NativeMethods.Read(0, new byte[10], 10);
                    break;
                case "throw":
                    throw new InvalidDataException($"The contents say {text}");
                case "crash":
                    if (text == "crash natively")
                    {
                        Marshal.WriteInt32(0x7fff_1234, 1);
                    }

                    Environment.FailFast($"The contents say {text}");
                    break;
                case "noise":
                    Console.Out.Write(new string('x', 1_000_000));
                    Console.Error.Write(new string('x', 1_000_000));
                    break;
                case "temporary":
                    string temporary = Path.GetTempFileName();
                    await File.WriteAllTextAsync(temporary, text);
                    await File.AppendAllTextAsync(_spec.LogPath, temporary + "\n");
                    await File.WriteAllTextAsync("in-working-folder", text);
                    break;
                case "full":
                    throw new IOException("The disk is full", OperatingSystem.IsWindows() ? unchecked((int)0x80070070) : 28);
                case "filled":
                    File.CreateSymbolicLink("thumbnail", "/dev/full");
                    break;
                case "in":
                    foreach (DerivedKind kind in kinds)
                    {
                        Directory.CreateDirectory(DerivedKinds.NameOf(kind));
                    }

                    break;
                case "undeletable":
                    DirectoryInfo kept = Directory.CreateDirectory("undeletable");
                    await File.WriteAllTextAsync(Path.Combine(kept.FullName, "file"), text);
                    if (!OperatingSystem.IsWindows())
                    {
                        File.SetUnixFileMode(kept.FullName, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                    }

                    break;
                case "child":
                    string[] child = text.Split(' ');
                    using (Process started = Process.Start("sleep", "1000"))
                    {
                        RunSignals.Say(child[2] + ".child", started.Id);
                    }

                    if (child[1] == "crash")
                    {
                        Environment.FailFast($"The contents say {text}");
                    }

                    await RunSignals.WaitAsync(child[2]);
                    break;
                case "wait":
                    await RunSignals.WaitAsync(text.Split(' ')[1]);
                    break;
            }

            foreach (DerivedKind kind in kinds.Where(kind => text != "without preview" || kind != DerivedKind.Preview))
            {
                try
                {
                    await MakeAsync(made, kind, text);
                }
                catch (IOException) when (text is "large thumbnail" or "filled disk")
                {
                }
            }

            switch (text.Split('\n')[0])
            {
                case "large by path":
                    await File.WriteAllBytesAsync("thumbnail", new byte[DerivedFileLimits.MaxBytesOf(DerivedKind.Thumbnail) + 1]);
                    break;
                case "linked":
                    File.Delete("thumbnail");
                    File.CreateSymbolicLink("thumbnail", Path.GetFullPath("preview"));
                    break;
                case "reads on":
                    Thread reading = new Thread(() => contents.ReadByte());
                    contents.Position = 0;
                    reading.Start();
                    while (reading.ThreadState != System.Threading.ThreadState.WaitSleepJoin)
                    {
                        Thread.Sleep(1);
                    }

                    break;
            }

            if (text == "other kinds")
            {
                await AddAsync(made, DerivedKind.Full, text);
            }
            else if (text == "kinds twice")
            {
                await AddAsync(made, kinds[0], text);
            }
        }

        private static List<DeclaredKind> MakesOf(Spec spec)
        {
            return spec.Kinds.Select(kind => new DeclaredKind(kind.Kind, kind.Version, MediaTypeSet.Of(kind.MediaTypes))).ToList();
        }

        private Task MakeAsync(IMadeFiles made, DerivedKind kind, string text)
        {
            if (kind == DerivedKind.Agent && text.StartsWith("picture", StringComparison.Ordinal))
            {
                // As much of a JPEG as tells one.
                return made.AddAsync(kind, MediaType, file => file.WriteAsync(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }).AsTask());
            }

            return kind == DerivedKind.Thumbnail && text == "large thumbnail"
                ? AddAsync(made, kind, new string('x', (int)DerivedFileLimits.MaxBytesOf(kind)), "x")
                : AddAsync(made, kind, Made(kind, text, _spec.Id, _spec.Kinds.First(spec => spec.Kind == kind).Version));
        }

        private static Task AddAsync(IMadeFiles made, DerivedKind kind, params string[] parts)
        {
            return made.AddAsync(kind, MediaType, async file =>
            {
                foreach (string part in parts)
                {
                    await file.WriteAsync(Encoding.UTF8.GetBytes(part));
                }
            });
        }

        private static void Eat(bool native)
        {
            List<byte[]> kept = new List<byte[]>();
            while (true)
            {
                kept.Add(Take(16 * 1024 * 1024, native));
            }
        }

        /// <summary>Takes memory, and keeps it when it is native; otherwise the answer is what holds it.</summary>
        private static byte[] Take(int bytes, bool native)
        {
            if (!native)
            {
                return Enumerable.Repeat((byte)1, bytes).ToArray();
            }

            byte[] page = Enumerable.Repeat((byte)1, 1024 * 1024).ToArray();
            nint taken = Marshal.AllocHGlobal(bytes);
            for (int offset = 0; offset + page.Length <= bytes; offset += page.Length)
            {
                Marshal.Copy(page, 0, taken + offset, page.Length);
            }

            return [];
        }

        private static class NativeMethods
        {
            [DllImport("libc", EntryPoint = "pause")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            public static extern int Pause();

            [DllImport("libc", EntryPoint = "write", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            public static extern nint Write(int descriptor, string text, nint count);

            [DllImport("libc", EntryPoint = "read")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            public static extern nint Read(int descriptor, byte[] buffer, nint count);
        }

        /// <param name="LogPath">The file each run writes a line to: the contents, then the kinds asked for.</param>
        public sealed record Spec(string Id, IReadOnlyList<KindSpec> Kinds, string LogPath)
        {
            public const string DefaultId = "test-maker";
        }

        public sealed record KindSpec(DerivedKind Kind, IReadOnlyList<string> MediaTypes, int Version);
    }
}
