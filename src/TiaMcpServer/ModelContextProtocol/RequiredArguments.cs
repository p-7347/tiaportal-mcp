using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// A call that leaves out a required argument used to fail inside the SDK with "The arguments
    /// dictionary is missing a value for the required parameter 'action'", which names neither the
    /// allowed values nor the other missing arguments (seen 2026-11-14 and 11-16 on EditBlockNetwork
    /// and EditBlockInterface). This filter checks the call against the tool's own schema first and
    /// answers with every missing argument and its description, so the caller can fix the call in one go.
    /// Registered after ToolDefaults.Apply, which takes preset arguments out of 'required'.
    /// </summary>
    public static class RequiredArguments
    {
        private static readonly Dictionary<string, List<KeyValuePair<string, string>>> _required =
            new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.Ordinal);

        public static McpServerTool Register(McpServerTool tool)
        {
            try
            {
                var schema = tool.ProtocolTool.InputSchema;
                var list = new List<KeyValuePair<string, string>>();

                if (schema.ValueKind == JsonValueKind.Object &&
                    schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
                {
                    schema.TryGetProperty("properties", out var properties);

                    foreach (var item in required.EnumerateArray())
                    {
                        var name = item.GetString();
                        if (string.IsNullOrEmpty(name))
                        {
                            continue;
                        }

                        var description = properties.ValueKind == JsonValueKind.Object &&
                                          properties.TryGetProperty(name!, out var property) &&
                                          property.ValueKind == JsonValueKind.Object &&
                                          property.TryGetProperty("description", out var text) &&
                                          text.ValueKind == JsonValueKind.String
                            ? text.GetString() ?? string.Empty
                            : string.Empty;

                        list.Add(new KeyValuePair<string, string>(name!, description));
                    }
                }

                lock (_required)
                {
                    _required[tool.ProtocolTool.Name] = list;
                }
            }
            catch
            {
                // Best effort: without the list the SDK's own message is used, as before.
            }

            return tool;
        }

        /// <summary>The message for the missing required arguments of a call, or null when none is missing.</summary>
        public static string? Check(string tool, IDictionary<string, JsonElement>? arguments)
        {
            List<KeyValuePair<string, string>>? required;
            lock (_required)
            {
                if (!_required.TryGetValue(tool, out required))
                {
                    return null;
                }
            }

            var missing = required
                .Where(r => arguments == null || !arguments.TryGetValue(r.Key, out var value) ||
                            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                .ToList();

            if (missing.Count == 0)
            {
                return null;
            }

            return $"'{tool}' needs {(missing.Count == 1 ? "the argument" : "the arguments")} " +
                   string.Join("; ", missing.Select(m => m.Value.Length > 0 ? $"'{m.Key}' ({m.Value})" : $"'{m.Key}'")) +
                   ". Nothing was changed; add " + (missing.Count == 1 ? "it" : "them") + " and call again.";
        }

        public static McpRequestFilter<CallToolRequestParams, CallToolResult> Filter =>
            next => (context, cancellationToken) =>
            {
                var name = context.Params?.Name;
                var message = name == null ? null : Check(name, context.Params!.Arguments);

                if (message != null)
                {
                    throw new McpException(message);
                }

                return next(context, cancellationToken);
            };
    }
}
