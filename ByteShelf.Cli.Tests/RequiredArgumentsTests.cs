using System.Text.Json;
using ByteShelf.Tools.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace ByteShelf.Cli.Tests
{
    /// <summary>Which required arguments a call leaves out, by the tool's own schema, and that both servers check for them.</summary>
    public sealed class RequiredArgumentsTests
    {
        private static readonly JsonElement MoveSchema = JsonElement.Parse("""{"type":"object","properties":{"path":{},"toPath":{},"shelf":{}},"required":["path","toPath"]}""");

        [Fact]
        public void MissingFrom_EveryRequiredGiven_IsNone()
        {
            Assert.Empty(RequiredArguments.MissingFrom(MoveSchema, Arguments("""{"path":"/a","toPath":"/b"}""")));
        }

        [Fact]
        public void MissingFrom_TwoLeftOut_NamesBothInTheSchemasOrder()
        {
            Assert.Equal(new[] { "path", "toPath" }, RequiredArguments.MissingFrom(MoveSchema, Arguments("""{"shelf":"docs"}""")));
        }

        [Fact]
        public void MissingFrom_NoArgumentsAtAll_NamesEveryRequiredOne()
        {
            Assert.Equal(new[] { "path", "toPath" }, RequiredArguments.MissingFrom(MoveSchema, null));
        }

        [Fact]
        public void MissingFrom_GivenAsNull_CountsAsLeftOut()
        {
            Assert.Equal(new[] { "toPath" }, RequiredArguments.MissingFrom(MoveSchema, Arguments("""{"path":"/a","toPath":null}""")));
        }

        [Fact]
        public void MissingFrom_SchemaWithoutRequired_IsNone()
        {
            JsonElement schema = JsonElement.Parse("""{"type":"object","properties":{"shelf":{}}}""");

            Assert.Empty(RequiredArguments.MissingFrom(schema, null));
        }

        [Fact]
        public void WithByteShelfTools_TheRegistrationBothServersUse_AddsTheCheck()
        {
            ServiceCollection services = new ServiceCollection();
            services.AddMcpServer().WithByteShelfTools(_ => ToolHost.Local);
            using ServiceProvider provider = services.BuildServiceProvider();

            McpServerOptions options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

            Assert.Single(options.Filters.Request.CallToolFilters);
        }

        [Fact]
        public void WithByteShelfTools_OnTheAgentsOwnMachine_SaysWhatDownloadFileDoesThere()
        {
            ServiceCollection services = new ServiceCollection();
            services.AddMcpServer().WithByteShelfTools(_ => ToolHost.Local);
            using ServiceProvider provider = services.BuildServiceProvider();

            McpServerTool download = Assert.Single(
                provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!, tool => tool.ProtocolTool.Name == "download_file");

            Assert.StartsWith("Reads a file from a shelf", download.ProtocolTool.Description, StringComparison.Ordinal);
            Assert.EndsWith("Save any other file with localPath, and look at a larger image there.", download.ProtocolTool.Description, StringComparison.Ordinal);
        }

        private static Dictionary<string, JsonElement> Arguments(string json)
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        }
    }
}
