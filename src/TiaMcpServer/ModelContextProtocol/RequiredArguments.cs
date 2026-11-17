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
    /// The same filter passes text arguments sent as a JSON value on as text (CoerceToText) and gives
    /// every failure that is not an McpException a message.
    /// </summary>
    public static class RequiredArguments
    {
        private static readonly Dictionary<string, List<KeyValuePair<string, string>>> _required =
            new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.Ordinal);

        private static readonly Dictionary<string, HashSet<string>> _stringArguments =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        public static McpServerTool Register(McpServerTool tool)
        {
            try
            {
                var schema = tool.ProtocolTool.InputSchema;
                var strings = new HashSet<string>(StringComparer.Ordinal);

                if (schema.ValueKind == JsonValueKind.Object &&
                    schema.TryGetProperty("properties", out var all) && all.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in all.EnumerateObject())
                    {
                        if (IsStringType(property.Value))
                        {
                            strings.Add(property.Name);
                        }
                    }
                }

                lock (_required)
                {
                    _stringArguments[tool.ProtocolTool.Name] = strings;
                }
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

        /// <summary>"type": "string" or ["string", "null"].</summary>
        private static bool IsStringType(JsonElement property)
        {
            if (property.ValueKind != JsonValueKind.Object || !property.TryGetProperty("type", out var type))
            {
                return false;
            }

            if (type.ValueKind == JsonValueKind.String)
            {
                return type.GetString() == "string";
            }

            return type.ValueKind == JsonValueKind.Array &&
                   type.EnumerateArray().Any(t => t.ValueKind == JsonValueKind.String && t.GetString() == "string") &&
                   type.EnumerateArray().All(t => t.ValueKind == JsonValueKind.String && (t.GetString() == "string" || t.GetString() == "null"));
        }

        /// <summary>
        /// Clients often send a text argument as the JSON value it describes: 'mapping' as an array
        /// instead of a string holding the array, a label text "1" as the number 1. The SDK then
        /// fails to bind the argument and the caller only sees "An error occurred invoking '...'"
        /// with no reason (SetHmiTagDynamization and CreateHmiScreenItem, field report 2026-11-17).
        /// Such a value is passed on as its JSON text, which is what the tool parses anyway.
        /// Returns the names that were converted.
        /// </summary>
        public static List<string> CoerceToText(string tool, IDictionary<string, JsonElement>? arguments)
        {
            var converted = new List<string>();
            HashSet<string>? strings;

            lock (_required)
            {
                _stringArguments.TryGetValue(tool, out strings);
            }

            if (strings == null || arguments == null)
            {
                return converted;
            }

            foreach (var name in arguments.Keys.ToList())
            {
                var value = arguments[name];

                if (!strings.Contains(name) ||
                    value.ValueKind is JsonValueKind.String or JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    continue;
                }

                // GetRawText of a number, true/false, an array or an object is the text a caller would have typed.
                using (var document = JsonDocument.Parse(JsonSerializer.Serialize(value.GetRawText())))
                {
                    arguments[name] = document.RootElement.Clone();
                }

                converted.Add(name);
            }

            return converted;
        }

        public static McpRequestFilter<CallToolRequestParams, CallToolResult> Filter =>
            next => async (context, cancellationToken) =>
            {
                var name = context.Params?.Name;

                if (name == null)
                {
                    return await next(context, cancellationToken);
                }

                var message = Check(name, context.Params!.Arguments);

                if (message != null)
                {
                    throw new McpException(message);
                }

                CoerceToText(name, context.Params.Arguments);

                try
                {
                    return await next(context, cancellationToken);
                }
                catch (Exception ex) when (ex is not McpException && ex is not OperationCanceledException)
                {
                    // The SDK shows the message of an McpException only; anything else reaches the caller as
                    // "An error occurred invoking '...'" with no reason. Say what it was.
                    throw new McpException($"'{name}' could not be called: {TiaMcpServer.Siemens.Operation.DescribeChain(ex)}", ex);
                }
            };
    }
}
