using System.Text.Json.Nodes;
using ByteShelf.Plugins;
using ByteShelf.Server.Derived.Services;
using ByteShelf.Server.Metadata.Services;
using ByteShelf.Server.Plugins.Models;
using ByteShelf.Server.Plugins.Processes;
using ByteShelf.Server.Plugins.Services;
using ByteShelf.Server.Storage.Models;
using ByteShelfCommon;

namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>
    /// A plugin's reader of text files that does, in its child process, what the contents say: <c>throw</c>,
    /// <c>crash</c>, <c>hang &lt;path&gt;</c> (once it has said so as <c>wait</c> does), <c>long</c> (answers more than a
    /// process may), <c>large</c> (more than a document may be), <c>nothing</c> or <c>wait &lt;path&gt;</c> (as
    /// <see cref="TestMaker"/> does). Anything else, and <c>wait</c>, it answers as <c>{"text": …}</c>.
    /// </summary>
    public sealed class TestReader : IContentDocumentReader
    {
        /// <summary>Where its process finds it.</summary>
        public const string Location = "reader";

        /// <summary>The plugin as the server loads it.</summary>
        public static LoadedPlugin Plugin(string id)
        {
            PluginDeclaration declaration = new PluginDeclaration(id, id, []);
            return new LoadedPlugin(new PluginManifest(declaration, "1.0.0", null), null, Location, Registration(), null);
        }

        /// <summary>What its child process does.</summary>
        internal static PluginWorks LoadWork()
        {
            return PluginWorks.Of([new ReadWork(), new MakeWork()], new PluginRegistrations(Registration(), null));
        }

        private static PluginReaderRegistration Registration()
        {
            return new PluginReaderRegistration(() => new TestReader(), 1, MediaTypeSet.Of("text/plain"));
        }

        public async Task<JsonObject?> ReadAsync(Stream contents)
        {
            using StreamReader reader = new StreamReader(contents, leaveOpen: true);
            string text = await reader.ReadToEndAsync();
            string[] words = text.Split(' ');
            switch (words[0])
            {
                case "throw":
                    throw new InvalidDataException($"The contents say {text}");
                case "crash":
                    Environment.FailFast($"The contents say {text}");
                    break;
                case "hang":
                    RunSignals.Say(words[1] + ".started", Environment.ProcessId);
                    Thread.Sleep(Timeout.Infinite);
                    break;
                case "long":
                    return new JsonObject { ["text"] = new string('x', Frame.MaxPayloadBytes) };
                case "large":
                    return new JsonObject { ["text"] = new string('x', PluginDocument.MaxBytes) };
                case "nothing":
                    return null;
                case "wait":
                    await RunSignals.WaitAsync(words[1]);
                    break;
            }

            return new JsonObject { ["text"] = text };
        }
    }
}
