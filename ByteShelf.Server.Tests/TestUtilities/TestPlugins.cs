namespace ByteShelf.Server.Tests.TestUtilities
{
    /// <summary>
    /// A folder of plugins of a test's own, as <c>BYTESHELF_PLUGINS</c> names one, deleted on dispose.
    /// The plugin with a server assembly is ByteShelf.Server.Tests.Plugin, copied beside the tests.
    /// </summary>
    public sealed class TestPlugins : IDisposable
    {
        public const string ServerAssemblyName = "ByteShelf.Server.Tests.Plugin.dll";

        /// <summary>Why a plugin made with <see cref="AddFailingServerPlugin"/> is skipped.</summary>
        public const string FailingReason = "Registering its services failed: It was told to fail.";

        private static readonly string BuiltPlugin = System.IO.Path.Combine(AppContext.BaseDirectory, "test-plugin");

        private readonly string _home = System.IO.Path.Combine(AppContext.BaseDirectory, "test-plugins", Guid.NewGuid().ToString("N"));

        public TestPlugins()
        {
            Path = InHome("plugins");
            Directory.CreateDirectory(Path);
        }

        /// <summary>The folder whose folders are the plugins.</summary>
        public string Path { get; }

        /// <summary>A <c>plugin.json</c> this server takes.</summary>
        public static string Manifest(string id, string? serverAssembly = null, int? api = null)
        {
            string assembly = serverAssembly == null ? string.Empty : $", \"serverAssembly\": \"{serverAssembly}\"";
            return $$"""{ "id": "{{id}}", "name": "Test {{id}}", "version": "1.2.3", "api": {{api ?? Server.Plugins.PluginApi.Version}}{{assembly}} }""";
        }

        /// <summary>A path beside <see cref="Path"/>, where no plugin is looked for.</summary>
        public string InHome(string name)
        {
            return System.IO.Path.Combine(_home, name);
        }

        /// <summary>Makes a plugin folder holding <paramref name="manifest"/>, or no <c>plugin.json</c> when it is null, and answers its path.</summary>
        public string Add(string folderName, string? manifest)
        {
            string folder = System.IO.Path.Combine(Path, folderName);
            Directory.CreateDirectory(folder);
            if (manifest != null)
            {
                File.WriteAllText(System.IO.Path.Combine(folder, "plugin.json"), manifest);
            }

            return folder;
        }

        /// <summary>Makes a plugin with the built server assembly, and answers its folder.</summary>
        public string AddServerPlugin(string id)
        {
            string folder = Add(id, Manifest(id, ServerAssemblyName));
            foreach (string file in Directory.EnumerateFiles(BuiltPlugin))
            {
                File.Copy(file, System.IO.Path.Combine(folder, System.IO.Path.GetFileName(file)));
            }

            return folder;
        }

        /// <summary>Makes a plugin whose registration throws an <see cref="InvalidOperationException"/> saying <see cref="FailingReason"/>, and answers its folder.</summary>
        public string AddFailingServerPlugin(string id)
        {
            string folder = AddServerPlugin(id);
            Write(folder, "fail-registration", string.Empty);
            return folder;
        }

        /// <summary>Makes a plugin whose registration fails unless the service <paramref name="serviceName"/> is registered before it, and answers its folder.</summary>
        public string AddServerPluginNeeding(string id, string serviceName)
        {
            string folder = AddServerPlugin(id);
            Write(folder, "needed-service", serviceName);
            return folder;
        }

        /// <summary>Writes a file of the plugin in <paramref name="folder"/>, making the folders on its way.</summary>
        public static void Write(string folder, string relativePath, string contents)
        {
            string path = System.IO.Path.Combine(folder, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_home, recursive: true);
            }
            catch (UnauthorizedAccessException)
            {
                // Windows keeps an assembly that was loaded; it is left in the test output.
            }
        }
    }
}
