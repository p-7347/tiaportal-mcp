using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// Read tools that fail get a line in 'yyyyMMdd-read-errors.jsonl' next to it (not read by GetChangeLog).
    /// Writes one JSON line per write tool call ([WriteTool]) to
    /// '&lt;backup path&gt;\audit\yyyyMMdd.jsonl': time, tool, arguments, ok or error, the reply
    /// message and the backup file. Read back with 'GetChangeLog', so a change list ("what did the
    /// server change today, and where are the backups") comes from what the server actually did.
    ///
    /// Callers: Program.cs (call-tool filter), McpServer.GetChangeLog. Tools invoked directly (the
    /// MSTest suite) bypass it. A log line that cannot be written never fails the call.
    /// </summary>
    public static class AuditLog
    {
        private static readonly object _lock = new object();

        private static readonly HashSet<string> _writeTools = new HashSet<string>(
            typeof(McpServer).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.GetCustomAttribute<WriteToolAttribute>() != null)
                .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name ?? m.Name),
            StringComparer.Ordinal);

        public static string Directory => Path.Combine(TiaMcpServer.Siemens.Portal.ResolveBackupRoot(), "audit");

        public static McpRequestFilter<CallToolRequestParams, CallToolResult> Filter =>
            next => async (context, cancellationToken) =>
            {
                var name = context.Params?.Name;
                if (name == null)
                {
                    return await next(context, cancellationToken);
                }

                if (!_writeTools.Contains(name))
                {
                    // Read tools: only failures, in a separate file that GetChangeLog does not read, so a
                    // later look can tell what really failed (e.g. a search refused while the PLC was online).
                    var at = DateTime.Now;
                    try
                    {
                        var read = await next(context, cancellationToken);
                        if (read.IsError == true)
                        {
                            Write(at, name, context.Params!.Arguments, "error", MessageOf(read), null, ReadErrorsSuffix);
                        }

                        return read;
                    }
                    catch (Exception ex)
                    {
                        Write(at, name, context.Params!.Arguments, "error", ex.Message, null, ReadErrorsSuffix);
                        throw;
                    }
                }

                var started = DateTime.Now;

                try
                {
                    var result = await next(context, cancellationToken);
                    Write(started, name, context.Params!.Arguments, result.IsError == true ? "error" : "ok", MessageOf(result), BackupOf(result));
                    return result;
                }
                catch (Exception ex)
                {
                    Write(started, name, context.Params!.Arguments, "error", ex.Message, null);
                    throw;
                }
            };

        /// <summary>The 'message' field of the structured reply, else the reply text.</summary>
        private static string? MessageOf(CallToolResult result) =>
            FieldOf(result, "message") ?? result.Content?.OfType<TextContentBlock>().Select(t => t.Text).FirstOrDefault();

        /// <summary>The 'backup' field of the reply (a path, or several joined by '; '), when the tool has one.</summary>
        private static string? BackupOf(CallToolResult result) => FieldOf(result, "backup");

        private static string? FieldOf(CallToolResult result, string field)
        {
            try
            {
                // Tools without structured output (e.g. ImportXmlBlock) return the same JSON as text.
                JsonElement element;
                if (result.StructuredContent != null)
                {
                    element = JsonSerializer.SerializeToElement(result.StructuredContent);
                }
                else if (result.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text is { } text && text.TrimStart().StartsWith("{", StringComparison.Ordinal))
                {
                    element = JsonDocument.Parse(text).RootElement;
                }
                else
                {
                    return null;
                }

                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(field, out var value))
                {
                    return null;
                }

                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Array => string.Join("; ", value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString())),
                    _ => null
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>File name suffix of the read-tool failure log: 'yyyyMMdd-read-errors.jsonl'.</summary>
        private const string ReadErrorsSuffix = "-read-errors";

        private static void Write(DateTime time, string tool, IDictionary<string, JsonElement>? arguments, string outcome, string? message, string? backup,
            string fileSuffix = "")
        {
            try
            {
                var entry = new JsonObject
                {
                    ["time"] = time.ToString("yyyy-MM-ddTHH:mm:ss"),
                    ["tool"] = tool,
                    ["outcome"] = outcome,
                    ["arguments"] = arguments == null ? new JsonObject() : JsonNode.Parse(JsonSerializer.Serialize(arguments)),
                    ["message"] = message == null ? null : (message.Length > 2000 ? message.Substring(0, 2000) + "..." : message),
                    ["backup"] = backup
                };

                var line = entry.ToJsonString() + Environment.NewLine;

                lock (_lock)
                {
                    System.IO.Directory.CreateDirectory(Directory);
                    File.AppendAllText(Path.Combine(Directory, time.ToString("yyyyMMdd") + fileSuffix + ".jsonl"), line, new UTF8Encoding(false));
                }
            }
            catch
            {
                // The change log must never break the change itself.
            }
        }

        /// <summary>Entries of one day, oldest first, optionally only tools matching 'toolRegex'.</summary>
        public static List<JsonObject> Read(DateTime day, string toolRegex)
        {
            var file = Path.Combine(Directory, day.ToString("yyyyMMdd") + ".jsonl");
            var entries = new List<JsonObject>();
            if (!File.Exists(file))
            {
                return entries;
            }

            var filter = string.IsNullOrWhiteSpace(toolRegex)
                ? null
                : new System.Text.RegularExpressions.Regex(toolRegex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            lock (_lock)
            {
                foreach (var line in File.ReadAllLines(file))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    try
                    {
                        if (JsonNode.Parse(line) is JsonObject entry
                            && (filter == null || filter.IsMatch((string?)entry["tool"] ?? string.Empty)))
                        {
                            entries.Add(entry);
                        }
                    }
                    catch (JsonException)
                    {
                        // A torn line (e.g. the server was killed while writing) is skipped.
                    }
                }
            }

            return entries;
        }
    }
}
