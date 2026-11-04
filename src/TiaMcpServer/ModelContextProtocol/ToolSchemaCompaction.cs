using ModelContextProtocol.Server;
using System;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// Shortens what 'tools/list' sends without changing any tool: every parameter description in
    /// the sources starts with its own name ("softwarePath: the PLC software ..."), which the schema
    /// already carries as the property key. With ~200 tools and ~700 parameters the prefixes alone
    /// were several thousand tokens on every request of a client that loads all tools. A plain
    /// "(default false)" / "(default 200)" is dropped too when the schema's own 'default' says the same.
    ///
    /// Callers: Program.BuildTools, after the other schema steps. Affected API: description text of
    /// the input schema only; names, types, defaults and required lists are unchanged.
    /// </summary>
    public static class ToolSchemaCompaction
    {
        private static readonly Regex PlainDefault = new(@"\s*\(default (?<v>true|false|-?\d+)\)", RegexOptions.CultureInvariant);

        public static McpServerTool Apply(McpServerTool tool)
        {
            if (JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText()) is not JsonObject schema ||
                schema["properties"] is not JsonObject properties)
            {
                return tool;
            }

            var changed = false;

            foreach (var property in properties)
            {
                if (property.Value is not JsonObject value ||
                    value["description"]?.GetValue<string>() is not string description)
                {
                    continue;
                }

                var text = description;
                if (text.StartsWith(property.Key + ":", StringComparison.Ordinal))
                {
                    text = text.Substring(property.Key.Length + 1).TrimStart();
                }

                if (value["default"] is JsonValue schemaDefault)
                {
                    var defaultText = schemaDefault.ToJsonString();
                    text = PlainDefault.Replace(text, m => m.Groups["v"].Value == defaultText ? string.Empty : m.Value);
                }

                if (text != description)
                {
                    value["description"] = text;
                    changed = true;
                }
            }

            if (changed)
            {
                using (var document = JsonDocument.Parse(schema.ToJsonString()))
                {
                    tool.ProtocolTool.InputSchema = document.RootElement.Clone();
                }
            }

            return tool;
        }
    }
}
