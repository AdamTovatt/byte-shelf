using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ByteShelf.Tools.Mcp
{
    /// <summary>
    /// A call-tool filter both MCP servers add: a call that leaves out an argument the tool's own schema
    /// requires is answered with an error naming it, such as "move needs toPath.". Without it the SDK's
    /// binder throws first and the agent reads only "An error occurred invoking 'move'", with nothing
    /// to correct.
    /// </summary>
    internal static class RequiredArguments
    {
        public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(McpRequestHandler<CallToolRequestParams, CallToolResult> next)
        {
            return async (context, cancellationToken) =>
            {
                if (context.MatchedPrimitive is McpServerTool tool && context.Params is CallToolRequestParams call)
                {
                    List<string> missing = MissingFrom(tool.ProtocolTool.InputSchema, call.Arguments);
                    if (missing.Count > 0)
                    {
                        return new CallToolResult
                        {
                            IsError = true,
                            Content = new List<ContentBlock> { new TextContentBlock { Text = $"{tool.ProtocolTool.Name} needs {string.Join(" and ", missing)}." } },
                        };
                    }
                }

                return await next(context, cancellationToken);
            };
        }

        /// <summary>The names the schema's <c>required</c> lists that <paramref name="arguments"/> leaves out or gives as null.</summary>
        internal static List<string> MissingFrom(JsonElement schema, IDictionary<string, JsonElement>? arguments)
        {
            List<string> missing = new List<string>();
            if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("required", out JsonElement required) || required.ValueKind != JsonValueKind.Array)
            {
                return missing;
            }

            foreach (JsonElement name in required.EnumerateArray())
            {
                string? argument = name.GetString();
                if (argument != null && (arguments == null || !arguments.TryGetValue(argument, out JsonElement value) || value.ValueKind == JsonValueKind.Null))
                {
                    missing.Add(argument);
                }
            }

            return missing;
        }
    }
}
