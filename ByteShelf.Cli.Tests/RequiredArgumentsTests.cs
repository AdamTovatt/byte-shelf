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
        private static readonly JsonElement MoveSchema = JsonDocument.Parse("""{"type":"object","properties":{"path":{},"toPath":{},"shelf":{}},"required":["path","toPath"]}""").RootElement;

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
            JsonElement schema = JsonDocument.Parse("""{"type":"object","properties":{"shelf":{}}}""").RootElement;

            Assert.Empty(RequiredArguments.MissingFrom(schema, null));
        }

        [Fact]
        public void WithByteShelfTools_TheRegistrationBothServersUse_AddsTheCheck()
        {
            ServiceCollection services = new ServiceCollection();
            services.AddMcpServer().WithByteShelfTools();
            using ServiceProvider provider = services.BuildServiceProvider();

            McpServerOptions options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

            Assert.Single(options.Filters.Request.CallToolFilters);
        }

        private static Dictionary<string, JsonElement> Arguments(string json)
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        }
    }
}
