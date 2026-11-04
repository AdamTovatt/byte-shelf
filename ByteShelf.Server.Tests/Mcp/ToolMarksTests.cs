using System.Reflection;
using ByteShelf.Tools.Mcp;
using ModelContextProtocol.Server;

namespace ByteShelf.Server.Tests.Mcp
{
    /// <summary>The marks <c>/mcp</c> offers the tools by.</summary>
    public sealed class ToolMarksTests
    {
        [Fact]
        public void EveryTool_IsMarkedWithWhatItNeeds()
        {
            List<MethodInfo> tools = typeof(ByteShelfMcpTools).GetMethods()
                .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() != null)
                .ToList();

            // The server offers a tool without the mark to no one, so a new tool would silently go missing there.
            Assert.NotEmpty(tools);
            Assert.All(tools, tool => Assert.NotNull(tool.GetCustomAttribute<ToolNeedsAttribute>()));
        }
    }
}
