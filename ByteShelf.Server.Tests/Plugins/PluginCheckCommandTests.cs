using ByteShelf.Server.Plugins.Services;
using ByteShelf.Server.Storage.Services;
using ByteShelf.Server.Tests.TestUtilities;

namespace ByteShelf.Server.Tests.Plugins
{
    /// <summary><c>--check-plugin</c>, whose exit code and output updaemon acts on.</summary>
    public sealed class PluginCheckCommandTests : IDisposable
    {
        private readonly TestPlugins _plugins = new TestPlugins();
        private readonly StringWriter _output = new StringWriter();
        private readonly StringWriter _errors = new StringWriter();

        [Fact]
        public void Run_APluginThisServerLoads_IsZero()
        {
            Assert.Equal(0, Run(_plugins.AddServerPlugin("server")));
            Assert.Equal("This server loads the plugin server 1.2.3.", _output.ToString().Trim());
            Assert.Empty(_errors.ToString());
        }

        [Fact]
        public void Run_ARegistrationThatThrows_IsOneWithTheReason()
        {
            Assert.Equal(1, Run(_plugins.AddFailingServerPlugin("failing")));
            Assert.Equal(TestPlugins.FailingReason, _errors.ToString().Trim());
        }

        [Fact]
        public void Run_APluginNeedingAServiceOfTheServers_IsZero()
        {
            // As one that wraps it looks for it: the check registers what the server does.
            Assert.Equal(0, Run(_plugins.AddServerPluginNeeding("wrapping", typeof(IShelfService).FullName!)));
        }

        [Fact]
        public void Run_APluginNeedingAServiceThatIsNotThere_IsOneWithTheReason()
        {
            Assert.Equal(1, Run(_plugins.AddServerPluginNeeding("wrapping", "Missing")));
            Assert.Equal("Registering its services failed: There is no Missing.", _errors.ToString().Trim());
        }

        [Theory]
        [InlineData("'Text/plain' is not a lowercase media type, such as image/png, or a family of them, such as image/*.", "1 Text/plain")]
        [InlineData("A plugin has one reader at most.", "1 text/plain", "1 text/html")]
        public void Run_AReaderThatIsNotAllowed_IsOneWithTheReason(string because, params string[] readers)
        {
            Assert.Equal(1, Run(_plugins.AddReadingServerPlugin("reading", readers)));
            Assert.Equal($"Registering its services failed: {because}", _errors.ToString().Trim());
        }

        [Fact]
        public void Run_AnotherApi_IsOneWithTheReason()
        {
            Assert.Equal(1, Run(_plugins.Add("markdown", TestPlugins.Manifest("markdown", api: 99))));
            Assert.Equal("It is for plugin api 99, and this server's is 1.", _errors.ToString().Trim());
        }

        [Fact]
        public void Run_NoSuchFolder_IsOne()
        {
            Assert.Equal(1, Run(Path.Combine(_plugins.Path, "missing")));
            Assert.Equal("There is no such folder.", _errors.ToString().Trim());
        }

        [Fact]
        public void Run_AControllerTheServerWouldFailToBuild_IsOneWithTheReason()
        {
            string folder = _plugins.Add("emitted", TestPlugins.Manifest("emitted", "Emitted.dll"));
            EmittedPlugin.Write(Path.Combine(folder, "Emitted.dll"), assembly =>
            {
                assembly.AddEntry("Entry");
                assembly.AddController("BrokenController", "{broken", isApiController: false);
            });

            Assert.Equal(1, Run(folder));
            Assert.StartsWith("Its controllers could not be built: ", _errors.ToString(), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("--check-plugin")]
        [InlineData("--check-plugin", "--help")]
        [InlineData("--check-plugin", "")]
        [InlineData("--check-plugin", "folder", "more")]
        [InlineData("--schema-only", "--check-plugin", "folder")]
        public void Run_WrongArguments_IsTwo(params string[] arguments)
        {
            Assert.Equal(2, PluginCheckCommand.Run(arguments, _output, _errors));
        }

        public void Dispose()
        {
            _plugins.Dispose();
            _output.Dispose();
            _errors.Dispose();
        }

        private int Run(string folder)
        {
            return PluginCheckCommand.Run(["--check-plugin", folder], _output, _errors);
        }
    }
}
